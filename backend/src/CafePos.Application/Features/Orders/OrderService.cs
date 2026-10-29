using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Application.Features.Inventory;
using CafePos.Application.Features.Locations;
using CafePos.Application.Payments;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Orders;

/// <summary>Cashiers see only their own orders; Managers and Admins see every order at the location.</summary>
public sealed class OrderService(
    ICurrentUser user,
    IOrderRepository orders,
    IOrderCatalogRepository catalog,
    ILocationRepository locations,
    StockService stock,
    IPaymentProviderResolver payments,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<OrderService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private bool ManagerUp => user.IsManagerUp();

    // ---------- queries ----------

    public async Task<Result<PagedResult<OrderDto>>> ListAsync(string? status, DateTime? from, DateTime? to, Guid? locationId, int? page, int? pageSize, CancellationToken ct)
    {
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        var (p, ps) = Paging.Normalize(page, pageSize);

        // Orders visible to the caller. Default: the active location (global query filter). Cross-location views are Owner only:
        // `locationId` of another location, or no active location and no locationId = all locations.
        var active = user.LocationId;
        OrderScopeKind scope;
        if (locationId is null || locationId == active)
        {
            if (active is not null) scope = OrderScopeKind.ActiveLocation;
            else if (!user.IsOwner) return Error.LocationRequired();
            else scope = OrderScopeKind.AllLocations;
        }
        else
        {
            if (!user.IsOwner) return Error.Forbidden("You do not have access to that location.");
            if (!await locations.ExistsAsync(locationId.Value, ct)) return Error.NotFound("Location");
            scope = OrderScopeKind.SpecificLocation;
        }

        OrderStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var st))
                return Error.BadRequest("Invalid status", "Status must be Open, Completed or Canceled.");
            parsed = st;
        }

        var page1 = await orders.ListAsync(new OrderListQuery(
            scope, scope == OrderScopeKind.SpecificLocation ? locationId : null, ManagerUp ? null : userId.Value, parsed,
            from?.ToUniversalTime(), to?.ToUniversalTime(), p, ps), ct);
        return new PagedResult<OrderDto>(page1.Orders.Select(o => o.ToDto()).ToList(), p, ps, page1.TotalCount);
    }

    private async Task<Result<Order>> GetEntityAsync(Guid id, bool track, CancellationToken ct)
    {
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        // Read-only lookups by an Owner may cross locations (explicit bypass of the location filter); anything that mutates stays in
        // the active location. Mutating paths need an active location (an Owner cross-location session gets a clear 400, not a 404).
        if (track && user.LocationId is null) return Error.LocationRequired();
        var crossRead = !track && user.IsOwner;
        var order = await orders.GetAsync(id, crossRead, track, ct);
        // Other location's orders are invisible (query filter); a cashier cannot see colleagues' orders.
        if (order is null || (!ManagerUp && order.CashierId != userId.Value)) return Error.NotFound("Order");
        return order;
    }

    public async Task<Result<OrderDto>> GetAsync(Guid id, CancellationToken ct) =>
        (await GetEntityAsync(id, false, ct)).Map(o => o.ToDto());

    public async Task<Result<ReceiptDto>> GetReceiptAsync(Guid id, CancellationToken ct)
    {
        var found = await GetEntityAsync(id, false, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;
        if (order.Status != OrderStatus.Completed || order.OrderNumber is null)
            return Error.Conflict("Receipt unavailable", "Receipts exist only for completed orders.");
        var loc = await locations.GetAsync(order.LocationId, ct);
        if (loc is null) return Error.NotFound("Location");
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

    /// <summary>Creates an OPEN order. Re-posting an existing id returns it unchanged (Created = false).</summary>
    public async Task<Result<CreateOrderOutcome>> CreateAsync(CreateOrderRequest req, CancellationToken ct)
    {
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        if (req.Id == Guid.Empty) return Error.BadRequest("Order id required", "Send a client-generated UUID as id.");
        if (user.LocationId is not { } locationId) return Error.LocationRequired(); // session without an active location
        var existing = await orders.FindOwnerAsync(req.Id, ct);
        if (existing is not null)
        {
            if (existing.LocationId != locationId || existing.CashierId != userId.Value)
                return Error.Conflict("Order id already in use", "An order with this id already exists.");
            return await ExistingAsync(req.Id, ct);
        }
        if (req.Items.Count == 0) return Error.Unprocessable("Empty order", "An order needs at least one item.");

        var loc = await locations.GetAsync(locationId, ct);
        if (loc is null) return Error.NotFound("Location");
        var order = new Order
        {
            Id = req.Id,
            LocationId = loc.Id,
            Status = OrderStatus.Open,
            CashierId = userId.Value,
            CashierName = user.Name,
            Note = req.Note,
            VatRatePercent = loc.VatRatePercent,
            CreatedAt = Now
        };
        foreach (var line in req.Items)
        {
            var built = await BuildLineAsync(order, line, ct);
            if (built.IsFailure) return built.Error!;
            order.Items.Add(built.Value);
        }
        Recalculate(order);

        orders.Add(order);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Concurrent create with the same client id: treat as idempotent.
            uow.ClearTracking();
            return await ExistingAsync(req.Id, ct);
        }
        logger.LogInformation("Order {OrderId} created by {UserId} at {LocationId} with {Lines} lines, total {Total}",
            order.Id, userId.Value, order.LocationId, order.Items.Count, order.Total);
        return new CreateOrderOutcome(order.ToDto(), true);
    }

    private async Task<Result<CreateOrderOutcome>> ExistingAsync(Guid id, CancellationToken ct)
    {
        // The id's creator re-posting: visible to them like to a manager (they own it).
        var order = await orders.GetAsync(id, user.IsOwner, false, ct);
        if (order is null) return Error.NotFound("Order");
        return new CreateOrderOutcome(order.ToDto(), false);
    }

    public async Task<Result<OrderDto>> AddItemAsync(Guid orderId, OrderLineRequest line, CancellationToken ct)
    {
        var found = await GetEntityAsync(orderId, true, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;
        var open = EnsureOpen(order);
        if (open.IsFailure) return open.Error!;
        var item = await BuildLineAsync(order, line, ct);
        if (item.IsFailure) return item.Error!;
        order.Items.Add(item.Value);
        Recalculate(order);
        await uow.SaveChangesAsync(ct);
        return order.ToDto();
    }

    /// <summary>Replaces the line; note the line gets a new item id.</summary>
    public async Task<Result<OrderDto>> UpdateItemAsync(Guid orderId, Guid itemId, OrderLineRequest line, CancellationToken ct)
    {
        var found = await GetEntityAsync(orderId, true, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;
        var open = EnsureOpen(order);
        if (open.IsFailure) return open.Error!;
        var existing = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (existing is null) return Error.NotFound("Order item");
        var replacement = await BuildLineAsync(order, line, ct);
        if (replacement.IsFailure) return replacement.Error!;

        orders.RemoveItem(existing);
        order.Items.Remove(existing);
        order.Items.Add(replacement.Value);
        Recalculate(order);
        await uow.SaveChangesAsync(ct);
        return order.ToDto();
    }

    public async Task<Result<OrderDto>> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct)
    {
        var found = await GetEntityAsync(orderId, true, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;
        var open = EnsureOpen(order);
        if (open.IsFailure) return open.Error!;
        var existing = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (existing is null) return Error.NotFound("Order item");
        if (order.Items.Count == 1) return Error.Unprocessable("Empty order", "Cancel the order instead of removing its last item.");
        orders.RemoveItem(existing);
        order.Items.Remove(existing);
        Recalculate(order);
        await uow.SaveChangesAsync(ct);
        return order.ToDto();
    }

    /// <summary>Cancel an OPEN order (before payment).</summary>
    public async Task<Result<OrderDto>> CancelAsync(Guid orderId, CancellationToken ct)
    {
        var found = await GetEntityAsync(orderId, true, ct);
        if (found.IsFailure) return found.Error!;
        var open = EnsureOpen(found.Value);
        if (open.IsFailure) return open.Error!;
        // Conditional claim so a concurrent completion cannot also win.
        if (await orders.TryCancelAsync(orderId, Now, ct) == 0) return Error.Conflict("Order is no longer open");
        uow.ClearTracking();
        logger.LogInformation("Order {OrderId} canceled by {UserId}", orderId, user.UserId);
        return await GetAsync(orderId, ct);
    }

    // ---------- completion ----------

    /// <summary>
    /// Completes an order: validates payment, then in ONE transaction claims the order (conditional UPDATE), assigns the receipt number,
    /// decrements stock (conditional UPDATE per ingredient + StockMovement rows), records the payment and the idempotency record.
    /// Replays the stored response when the same Idempotency-Key is used again for the same order.
    /// </summary>
    public async Task<Result<CompleteOrderOutcome>> CompleteAsync(Guid orderId, string? idempotencyKey, CompleteOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return Error.BadRequest("Idempotency-Key required", "Send an Idempotency-Key header (max 200 chars).");
        var key = idempotencyKey.Trim();

        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        if (user.LocationId is not { } locationId) return Error.LocationRequired();

        var record = await orders.GetIdempotencyRecordAsync(key, ct);
        if (record is not null)
        {
            var visible = await GetEntityAsync(orderId, false, ct); // enforce visibility before replaying
            if (visible.IsFailure) return visible.Error!;
            return Replay(record, orderId);
        }

        var found = await GetEntityAsync(orderId, true, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;
        var open = EnsureOpen(order);
        if (open.IsFailure) return open.Error!;
        if (order.Items.Count == 0) return Error.Unprocessable("Empty order");

        var loc = await locations.GetAsync(locationId, ct);
        if (loc is null) return Error.NotFound("Location");
        order.VatRatePercent = loc.VatRatePercent;
        Recalculate(order);

        var resolved = payments.Resolve(req.PaymentMethod);
        if (resolved.IsFailure) return resolved.Error!;
        var provider = resolved.Value;
        var result = await provider.ChargeAsync(new PaymentRequest(order.Id, order.Total, req.AmountTendered), ct);
        if (!result.Succeeded)
        {
            logger.LogWarning("Payment failed for order {OrderId} via {Method}: {Reason}", order.Id, provider.Method, result.FailureReason);
            return Error.Unprocessable("Payment failed", result.FailureReason);
        }

        await using var tx = await uow.BeginTransactionAsync(ct);

        // 1. Claim the order. If another request completed/canceled it meanwhile, this updates 0 rows.
        if (await orders.TryClaimForCompletionAsync(orderId, ct) == 0)
            return Error.Conflict("Order is no longer open", "This order was already completed or canceled.");

        // 2. Per-location receipt number (row lock on the location held until commit).
        var number = await orders.NextReceiptNumberAsync(locationId, ct);

        // 3. Inventory: conditional decrements + ledger rows (409 and rollback when short).
        var consumed = await stock.ConsumeForOrderAsync(order, userId.Value, ct);
        if (consumed.IsFailure) return consumed.Error!;

        // 4. Order + payment.
        var now = Now;
        order.Status = OrderStatus.Completed;
        order.OrderNumber = number;
        order.CompletedAt = now;
        order.Payments.Add(new Payment
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
        });

        var dto = order.ToDto();
        orders.AddIdempotencyRecord(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Key = key,
            OrderId = order.Id,
            StatusCode = 200,
            ResponseJson = JsonSerializer.Serialize(dto, Json),
            CreatedAt = now
        });

        try
        {
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Error.Conflict("Request in progress", "A request with this Idempotency-Key is already being processed. Retry shortly.");
        }

        logger.LogInformation("Order {OrderId} completed as #{Number} at {LocationId}: total {Total}, {Method}, cashier {UserId}",
            order.Id, number, locationId, order.Total, provider.Method, userId.Value);
        return new CompleteOrderOutcome(dto, false);
    }

    private Result<CompleteOrderOutcome> Replay(IdempotencyRecord record, Guid orderId)
    {
        if (record.OrderId != orderId)
            return Error.Unprocessable("Idempotency-Key reused", "This Idempotency-Key was already used for a different order.");
        logger.LogInformation("Replaying stored completion for order {OrderId} (idempotent retry)", orderId);
        return new CompleteOrderOutcome(JsonSerializer.Deserialize<OrderDto>(record.ResponseJson, Json)!, true);
    }

    // ---------- helpers ----------

    private static Result EnsureOpen(Order order) =>
        order.Status == OrderStatus.Open ? Result.Success() : Error.Conflict("Order is not open", $"Order is {order.Status}.");

    private static void Recalculate(Order order)
    {
        order.Total = PricingCalculator.Round(order.Items.Sum(i => i.LineTotal));
        order.VatAmount = PricingCalculator.ExtractVat(order.Total, order.VatRatePercent);
    }

    private async Task<Result<OrderItem>> BuildLineAsync(Order order, OrderLineRequest line, CancellationToken ct)
    {
        var variation = await catalog.GetVariationWithItemAsync(line.ItemVariationId, ct);
        if (variation is null || !variation.Active || variation.Item is null || !variation.Item.Active)
            return Error.Unprocessable("Item unavailable", $"Item variation {line.ItemVariationId} does not exist or is not for sale.");

        var modifierIds = (line.ModifierIds ?? new()).ToList();
        var modifiers = modifierIds.Count == 0 ? new List<Modifier>() : (await catalog.GetModifiersAsync(modifierIds, ct)).ToList();
        if (modifiers.Count != modifierIds.Distinct().Count())
            return Error.Unprocessable("Invalid modifier", "One or more modifiers do not exist.");

        var allowedListIds = variation.Item.ModifierLists.Select(l => l.ModifierListId).ToList();
        var lists = await catalog.GetActiveModifierListsAsync(allowedListIds, ct);
        foreach (var m in modifiers)
        {
            if (!m.Active || lists.All(l => l.Id != m.ModifierListId))
                return Error.Unprocessable("Invalid modifier", $"Modifier '{m.Name}' is not available for {variation.Item.Name}.");
        }
        foreach (var list in lists)
        {
            var n = modifiers.Count(m => m.ModifierListId == list.Id);
            if (n < list.MinSelections || n > list.MaxSelections)
                return Error.Unprocessable("Invalid modifier selection",
                    $"'{list.Name}' requires between {list.MinSelections} and {list.MaxSelections} selection(s) (got {n}).");
        }

        // The order's location may charge its own price for this variation; otherwise the global price applies.
        var priceOverride = await catalog.GetPriceOverrideAsync(variation.Id, order.LocationId, ct);
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
}
