using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using CafePos.Api.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CafePos.Api.Services;

public class OrderService
{
    private readonly CafeDbContext _db;
    private readonly ICurrentLocation _current;
    private readonly StockService _stock;
    private readonly IPaymentProviderResolver _payments;
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<OrderService> _logger;

    public OrderService(CafeDbContext db, ICurrentLocation current, StockService stock, IPaymentProviderResolver payments,
        IOptions<JsonOptions> json, ILogger<OrderService> logger)
    {
        _db = db;
        _current = current;
        _stock = stock;
        _payments = payments;
        _json = json.Value.JsonSerializerOptions;
        _logger = logger;
    }

    private Guid LocationId => _current.LocationId ?? throw ApiException.LocationRequired();

    // ---------- queries ----------

    public async Task<PagedResult<OrderDto>> ListAsync(Guid userId, bool managerUp, string? status, DateTime? from, DateTime? to,
        Guid? locationId, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);
        var q = await ScopedOrdersAsync(locationId, ct);
        if (!managerUp) q = q.Where(o => o.CashierId == userId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var st))
                throw ApiException.BadRequest("Invalid status", "Status must be Open, Completed or Canceled.");
            q = q.Where(o => o.Status == st);
        }
        if (from is not null) q = q.Where(o => o.CreatedAt >= from.Value.ToUniversalTime());
        if (to is not null) q = q.Where(o => o.CreatedAt < to.Value.ToUniversalTime());

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * ps).Take(ps)
            .Include(o => o.Items).ThenInclude(i => i.Modifiers).Include(o => o.Payments)
            .AsSplitQuery().ToListAsync(ct);
        return new PagedResult<OrderDto>(rows.Select(Map).ToList(), p, ps, total);
    }

    /// <summary>
    /// Orders visible to the caller. Default: the active location (global query filter). Cross-location views are Owner only and use
    /// an explicit IgnoreQueryFilters(): `locationId` of another location, or no active location and no locationId = all locations.
    /// </summary>
    private async Task<IQueryable<Order>> ScopedOrdersAsync(Guid? locationId, CancellationToken ct)
    {
        var active = _current.LocationId;
        if (locationId is null || locationId == active)
        {
            if (active is not null) return _db.Orders.AsNoTracking();
            if (!_current.IsOwner) throw ApiException.LocationRequired();
            return _db.Orders.IgnoreQueryFilters().AsNoTracking(); // Owner, cross-location session: every location
        }
        if (!_current.IsOwner) throw ApiException.Forbidden("You do not have access to that location.");
        if (!await _db.Locations.AnyAsync(l => l.Id == locationId, ct)) throw ApiException.NotFound("Location");
        return _db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.LocationId == locationId);
    }

    public async Task<Order> GetEntityAsync(Guid id, Guid userId, bool managerUp, bool track, CancellationToken ct)
    {
        // Read-only lookups by an Owner may cross locations (explicit IgnoreQueryFilters); anything that mutates stays in the active location.
        // Mutating paths need an active location (an Owner cross-location session gets a clear 400, not a misleading 404).
        if (track && _current.LocationId is null) throw ApiException.LocationRequired();
        var crossRead = !track && _current.IsOwner;
        var q = (crossRead ? _db.Orders.IgnoreQueryFilters() : _db.Orders)
            .Include(o => o.Items).ThenInclude(i => i.Modifiers).Include(o => o.Payments).AsSplitQuery();
        if (!track) q = q.AsNoTracking();
        var order = await q.FirstOrDefaultAsync(o => o.Id == id, ct);
        // Other location's orders are invisible (query filter); a cashier cannot see colleagues' orders.
        if (order is null || (!managerUp && order.CashierId != userId)) throw ApiException.NotFound("Order");
        return order;
    }

    public async Task<OrderDto> GetAsync(Guid id, Guid userId, bool managerUp, CancellationToken ct) =>
        Map(await GetEntityAsync(id, userId, managerUp, false, ct));

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, Guid userId, bool managerUp, CancellationToken ct)
    {
        var order = await GetEntityAsync(id, userId, managerUp, false, ct);
        if (order.Status != OrderStatus.Completed || order.OrderNumber is null)
            throw ApiException.Conflict("Receipt unavailable", "Receipts exist only for completed orders.");
        var loc = await _db.Locations.AsNoTracking().FirstAsync(l => l.Id == order.LocationId, ct);
        var pay = order.Payments.First(p => p.Status == PaymentStatus.Succeeded);
        var lines = order.Items.Select(i => new ReceiptLineDto(
            string.IsNullOrWhiteSpace(i.VariationName) ? i.ItemName : $"{i.ItemName} ({i.VariationName})",
            i.Modifiers.Count == 0 ? null : string.Join(", ", i.Modifiers.Select(m => m.Name)),
            i.Quantity, i.UnitPrice, i.LineTotal)).ToList();
        return new ReceiptDto(loc.Name, loc.Address, loc.ContactPhone, order.OrderNumber.Value, order.Id, order.CompletedAt!.Value,
            loc.TimeZone, order.CashierName, lines, true, order.VatRatePercent, order.Total - order.VatAmount, order.VatAmount, order.Total,
            pay.Method, pay.AmountTendered ?? pay.Amount, pay.ChangeGiven ?? 0m, "PHP");
    }

    // ---------- create / edit ----------

    /// <summary>Creates an OPEN order. Returns (order, created). Re-posting an existing id returns it unchanged (created=false).</summary>
    public async Task<(OrderDto order, bool created)> CreateAsync(CreateOrderRequest req, Guid userId, string userName, CancellationToken ct)
    {
        if (req.Id == Guid.Empty) throw ApiException.BadRequest("Order id required", "Send a client-generated UUID as id.");
        var locationId = LocationId; // read once, outside any EF expression: throws 400 for a session without an active location
        var existing = await _db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == req.Id)
            .Select(o => new { o.LocationId, o.CashierId }).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (existing.LocationId != locationId || existing.CashierId != userId)
                throw ApiException.Conflict("Order id already in use", "An order with this id already exists.");
            return (await GetAsync(req.Id, userId, true, ct), false);
        }
        if (req.Items.Count == 0) throw ApiException.Unprocessable("Empty order", "An order needs at least one item.");

        var loc = await _db.Locations.AsNoTracking().FirstAsync(l => l.Id == locationId, ct);
        var order = new Order
        {
            Id = req.Id,
            LocationId = loc.Id,
            Status = OrderStatus.Open,
            CashierId = userId,
            CashierName = userName,
            Note = req.Note,
            VatRatePercent = loc.VatRatePercent,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var line in req.Items)
            order.Items.Add(await BuildLineAsync(order, line, ct));
        Recalculate(order);

        _db.Orders.Add(order);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent create with the same client id: treat as idempotent.
            _db.ChangeTracker.Clear();
            return (await GetAsync(req.Id, userId, true, ct), false);
        }
        _logger.LogInformation("Order {OrderId} created by {UserId} at {LocationId} with {Lines} lines, total {Total}",
            order.Id, userId, order.LocationId, order.Items.Count, order.Total);
        return (Map(order), true);
    }

    public async Task<OrderDto> AddItemAsync(Guid orderId, OrderLineRequest line, Guid userId, bool managerUp, CancellationToken ct)
    {
        var order = await GetEntityAsync(orderId, userId, managerUp, true, ct);
        EnsureOpen(order);
        var item = await BuildLineAsync(order, line, ct);
        order.Items.Add(item);
        Recalculate(order);
        await _db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> UpdateItemAsync(Guid orderId, Guid itemId, OrderLineRequest line, Guid userId, bool managerUp, CancellationToken ct)
    {
        var order = await GetEntityAsync(orderId, userId, managerUp, true, ct);
        EnsureOpen(order);
        var existing = order.Items.FirstOrDefault(i => i.Id == itemId) ?? throw ApiException.NotFound("Order item");
        var replacement = await BuildLineAsync(order, line, ct);

        // The line is replaced (new item id), not patched in place.
        _db.OrderItems.Remove(existing);
        order.Items.Remove(existing);
        order.Items.Add(replacement);
        Recalculate(order);
        await _db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> RemoveItemAsync(Guid orderId, Guid itemId, Guid userId, bool managerUp, CancellationToken ct)
    {
        var order = await GetEntityAsync(orderId, userId, managerUp, true, ct);
        EnsureOpen(order);
        var existing = order.Items.FirstOrDefault(i => i.Id == itemId) ?? throw ApiException.NotFound("Order item");
        if (order.Items.Count == 1) throw ApiException.Unprocessable("Empty order", "Cancel the order instead of removing its last item.");
        _db.OrderItems.Remove(existing);
        order.Items.Remove(existing);
        Recalculate(order);
        await _db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> CancelAsync(Guid orderId, Guid userId, bool managerUp, CancellationToken ct)
    {
        var order = await GetEntityAsync(orderId, userId, managerUp, true, ct);
        EnsureOpen(order);
        // Conditional claim so a concurrent completion cannot also win.
        var claimed = await _db.Orders.Where(o => o.Id == orderId && o.Status == OrderStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Canceled).SetProperty(o => o.CanceledAt, DateTime.UtcNow), ct);
        if (claimed == 0) throw ApiException.Conflict("Order is no longer open");
        _db.ChangeTracker.Clear();
        _logger.LogInformation("Order {OrderId} canceled by {UserId}", orderId, userId);
        return await GetAsync(orderId, userId, managerUp, ct);
    }

    // ---------- completion ----------

    /// <summary>
    /// Completes an order: validates payment, then in ONE transaction claims the order (conditional UPDATE), assigns the receipt number,
    /// decrements stock (conditional UPDATE per ingredient + StockMovement rows), records the payment and the idempotency record.
    /// Replays the stored response when the same Idempotency-Key is used again for the same order.
    /// </summary>
    public async Task<(OrderDto order, bool replayed)> CompleteAsync(Guid orderId, string idempotencyKey, CompleteOrderRequest req,
        Guid userId, bool managerUp, CancellationToken ct)
    {
        var locationId = LocationId;

        var record = await _db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == idempotencyKey, ct);
        if (record is not null)
        {
            await GetEntityAsync(orderId, userId, managerUp, false, ct); // enforce visibility before replaying
            return (ReplayOrThrow(record, orderId), true);
        }

        var order = await GetEntityAsync(orderId, userId, managerUp, true, ct);
        EnsureOpen(order);
        if (order.Items.Count == 0) throw ApiException.Unprocessable("Empty order");

        var loc = await _db.Locations.AsNoTracking().FirstAsync(l => l.Id == locationId, ct);
        order.VatRatePercent = loc.VatRatePercent;
        Recalculate(order);

        var provider = _payments.Resolve(req.PaymentMethod);
        var result = await provider.ChargeAsync(new PaymentRequest(order.Id, order.Total, req.AmountTendered), ct);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Payment failed for order {OrderId} via {Method}: {Reason}", order.Id, provider.Method, result.FailureReason);
            throw ApiException.Unprocessable("Payment failed", result.FailureReason);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Claim the order. If another request completed/canceled it meanwhile, this updates 0 rows.
        var claimed = await _db.Orders.Where(o => o.Id == orderId && o.Status == OrderStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Completed), ct);
        if (claimed == 0) throw ApiException.Conflict("Order is no longer open", "This order was already completed or canceled.");

        // 2. Per-location receipt number (row lock on Locations held until commit).
        await _db.Locations.Where(l => l.Id == locationId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastOrderNumber, l => l.LastOrderNumber + 1), ct);
        var number = await _db.Locations.Where(l => l.Id == locationId).Select(l => l.LastOrderNumber).FirstAsync(ct);

        // 3. Inventory: conditional decrements + ledger rows (throws 409 and rolls back when short).
        await _stock.ConsumeForOrderAsync(order, userId, ct);

        // 4. Order + payment.
        var now = DateTime.UtcNow;
        order.Status = OrderStatus.Completed;
        order.OrderNumber = number;
        order.CompletedAt = now;
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            LocationId = locationId,
            Method = provider.Method,
            Status = PaymentStatus.Succeeded,
            Amount = result.Amount,
            AmountTendered = result.AmountTendered,
            ChangeGiven = result.ChangeGiven,
            ProviderReference = result.ProviderReference,
            CreatedAt = now
        };
        order.Payments.Add(payment);

        var dto = Map(order);
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Key = idempotencyKey,
            OrderId = order.Id,
            StatusCode = 200,
            ResponseJson = JsonSerializer.Serialize(dto, _json),
            CreatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ApiException.Conflict("Request in progress", "A request with this Idempotency-Key is already being processed. Retry shortly.");
        }

        _logger.LogInformation("Order {OrderId} completed as #{Number} at {LocationId}: total {Total}, {Method}, cashier {UserId}",
            order.Id, number, locationId, order.Total, provider.Method, userId);
        return (dto, false);
    }

    private OrderDto ReplayOrThrow(IdempotencyRecord record, Guid orderId)
    {
        if (record.OrderId != orderId)
            throw ApiException.Unprocessable("Idempotency-Key reused", "This Idempotency-Key was already used for a different order.");
        _logger.LogInformation("Replaying stored completion for order {OrderId} (idempotent retry)", orderId);
        return JsonSerializer.Deserialize<OrderDto>(record.ResponseJson, _json)!;
    }

    // ---------- helpers ----------

    private static void EnsureOpen(Order order)
    {
        if (order.Status != OrderStatus.Open)
            throw ApiException.Conflict("Order is not open", $"Order is {order.Status}.");
    }

    private static void Recalculate(Order order)
    {
        order.Total = PricingCalculator.Round(order.Items.Sum(i => i.LineTotal));
        order.VatAmount = PricingCalculator.ExtractVat(order.Total, order.VatRatePercent);
    }

    private async Task<OrderItem> BuildLineAsync(Order order, OrderLineRequest line, CancellationToken ct)
    {
        var variation = await _db.ItemVariations.AsNoTracking().Include(v => v.Item).ThenInclude(i => i!.ModifierLists)
            .FirstOrDefaultAsync(v => v.Id == line.ItemVariationId, ct);
        if (variation is null || !variation.Active || variation.Item is null || !variation.Item.Active)
            throw ApiException.Unprocessable("Item unavailable", $"Item variation {line.ItemVariationId} does not exist or is not for sale.");

        var modifierIds = (line.ModifierIds ?? new()).ToList();
        var modifiers = modifierIds.Count == 0
            ? new List<Modifier>()
            : await _db.Modifiers.AsNoTracking().Where(m => modifierIds.Contains(m.Id)).ToListAsync(ct);
        if (modifiers.Count != modifierIds.Distinct().Count())
            throw ApiException.Unprocessable("Invalid modifier", "One or more modifiers do not exist.");

        var allowedListIds = variation.Item.ModifierLists.Select(l => l.ModifierListId).ToList();
        var lists = await _db.ModifierLists.AsNoTracking().Where(l => allowedListIds.Contains(l.Id) && l.Active).ToListAsync(ct);
        foreach (var m in modifiers)
        {
            if (!m.Active || lists.All(l => l.Id != m.ModifierListId))
                throw ApiException.Unprocessable("Invalid modifier", $"Modifier '{m.Name}' is not available for {variation.Item.Name}.");
        }
        foreach (var list in lists)
        {
            var n = modifiers.Count(m => m.ModifierListId == list.Id);
            if (n < list.MinSelections || n > list.MaxSelections)
                throw ApiException.Unprocessable("Invalid modifier selection",
                    $"'{list.Name}' requires between {list.MinSelections} and {list.MaxSelections} selection(s) (got {n}).");
        }

        // The order's location may charge its own price for this variation; otherwise the global price applies.
        var priceOverride = await _db.ItemVariationLocationOverrides.AsNoTracking()
            .Where(o => o.ItemVariationId == variation.Id && o.LocationId == order.LocationId).Select(o => o.PriceOverride).FirstOrDefaultAsync(ct);
        var unit = PricingCalculator.Round((priceOverride ?? variation.Price) + modifiers.Sum(m => m.PriceDelta));
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            LocationId = order.LocationId,
            ItemVariationId = variation.Id,
            ItemName = variation.Item.Name,
            VariationName = variation.Name,
            Sku = variation.Sku,
            UnitPrice = unit,
            Quantity = line.Quantity,
            LineTotal = PricingCalculator.Round(unit * line.Quantity),
            Note = line.Note
        };
        foreach (var m in modifiers)
            item.Modifiers.Add(new OrderItemModifier
            {
                Id = Guid.NewGuid(), OrderItemId = item.Id, LocationId = order.LocationId, ModifierId = m.Id, Name = m.Name, PriceDelta = m.PriceDelta
            });
        return item;
    }

    public static OrderDto Map(Order o) => new(
        o.Id, o.OrderNumber, o.Status.ToString(), o.LocationId, o.CashierId, o.CashierName, o.Note, o.VatRatePercent,
        o.Total, o.Total - o.VatAmount, o.VatAmount, o.CreatedAt, o.CompletedAt, o.CanceledAt,
        o.Items.Select(i => new OrderItemDto(i.Id, i.ItemVariationId, i.ItemName, i.VariationName, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal, i.Note,
            i.Modifiers.Select(m => new OrderItemModifierDto(m.ModifierId, m.Name, m.PriceDelta)).ToList())).ToList(),
        o.Payments.Select(p => new PaymentDto(p.Id, p.Method, p.Status.ToString(), p.Amount, p.AmountTendered, p.ChangeGiven, p.CreatedAt)).ToList());
}
