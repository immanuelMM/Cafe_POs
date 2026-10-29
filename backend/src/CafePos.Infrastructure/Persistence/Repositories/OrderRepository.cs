using CafePos.Application.Features.Orders;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

/// <summary>
/// Orders carry the active-location query filter. The cross-location paths (IgnoreQueryFilters) exist for Owner reads and for the
/// global client-id uniqueness check; the service decides when they are allowed, never the caller of the API directly.
/// </summary>
internal sealed class OrderRepository(CafeDbContext db) : IOrderRepository
{
    public async Task<OrderPage> ListAsync(OrderListQuery query, CancellationToken ct)
    {
        var locationId = query.LocationId;
        var q = query.Scope switch
        {
            OrderScopeKind.ActiveLocation => db.Orders.AsNoTracking(),
            OrderScopeKind.AllLocations => db.Orders.IgnoreQueryFilters().AsNoTracking(),
            _ => db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.LocationId == locationId)
        };
        if (query.CashierId is { } cashier) q = q.Where(o => o.CashierId == cashier);
        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.From is { } from) q = q.Where(o => o.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(o => o.CreatedAt < to);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Include(o => o.Items).ThenInclude(i => i.Modifiers).Include(o => o.Payments)
            .AsSplitQuery().ToListAsync(ct);
        return new OrderPage(rows, total);
    }

    public Task<Order?> GetAsync(Guid id, bool crossLocation, bool track, CancellationToken ct)
    {
        var q = (crossLocation ? db.Orders.IgnoreQueryFilters() : db.Orders)
            .Include(o => o.Items).ThenInclude(i => i.Modifiers).Include(o => o.Payments).AsSplitQuery();
        if (!track) q = q.AsNoTracking();
        return q.FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public Task<OrderOwner?> FindOwnerAsync(Guid id, CancellationToken ct) =>
        db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == id)
            .Select(o => new OrderOwner(o.LocationId, o.CashierId)).FirstOrDefaultAsync(ct);

    public void Add(Order order) => db.Orders.Add(order);

    public void RemoveItem(OrderItem item) => db.OrderItems.Remove(item);

    public Task<int> TryCancelAsync(Guid orderId, DateTime canceledAt, CancellationToken ct) =>
        db.Orders.Where(o => o.Id == orderId && o.Status == OrderStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Canceled).SetProperty(o => o.CanceledAt, canceledAt), ct);

    public Task<int> TryClaimForCompletionAsync(Guid orderId, CancellationToken ct) =>
        db.Orders.Where(o => o.Id == orderId && o.Status == OrderStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Completed), ct);

    public async Task<int> NextReceiptNumberAsync(Guid locationId, CancellationToken ct)
    {
        await db.Locations.Where(l => l.Id == locationId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastOrderNumber, l => l.LastOrderNumber + 1), ct);
        return await db.Locations.Where(l => l.Id == locationId).Select(l => l.LastOrderNumber).FirstAsync(ct);
    }

    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct) =>
        db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);

    public void AddIdempotencyRecord(IdempotencyRecord record) => db.IdempotencyRecords.Add(record);
}
