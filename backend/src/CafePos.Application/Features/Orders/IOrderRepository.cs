using CafePos.Domain;

namespace CafePos.Application.Features.Orders;

/// <summary>
/// Orders (aggregate root with items, modifiers and payments), the per-location receipt counter and the idempotency records of
/// order completion. Orders carry the location query filter; the cross-location variants below bypass it explicitly and are only
/// reachable through the Owner checks in <see cref="OrderService"/>.
/// </summary>
public interface IOrderRepository
{
    /// <summary>Orders visible under <paramref name="query"/> with items, modifiers and payments (read-only), newest first.</summary>
    Task<OrderPage> ListAsync(OrderListQuery query, CancellationToken ct);

    /// <summary>
    /// Order with items, modifiers and payments. <paramref name="crossLocation"/> bypasses the active-location filter (Owner reads);
    /// <paramref name="track"/> returns a tracked entity for modification.
    /// </summary>
    Task<Order?> GetAsync(Guid id, bool crossLocation, bool track, CancellationToken ct);

    /// <summary>Location and cashier of an order with this id in ANY location (client-generated ids are globally unique), or null.</summary>
    Task<OrderOwner?> FindOwnerAsync(Guid id, CancellationToken ct);

    void Add(Order order);

    void RemoveItem(OrderItem item);

    /// <summary>Conditional UPDATE Open to Canceled. Returns the number of rows changed (0 when no longer open). Immediate.</summary>
    Task<int> TryCancelAsync(Guid orderId, DateTime canceledAt, CancellationToken ct);

    /// <summary>Conditional UPDATE Open to Completed, claiming the order. 0 when no longer open. Immediate (inside the caller's transaction).</summary>
    Task<int> TryClaimForCompletionAsync(Guid orderId, CancellationToken ct);

    /// <summary>Atomically increments the location's receipt counter and returns the new number (row lock held until commit).</summary>
    Task<int> NextReceiptNumberAsync(Guid locationId, CancellationToken ct);

    /// <summary>Stored completion for an Idempotency-Key at the active location, or null.</summary>
    Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct);

    void AddIdempotencyRecord(IdempotencyRecord record);
}

public enum OrderScopeKind
{
    /// <summary>The caller's active location (the global query filter applies).</summary>
    ActiveLocation,

    /// <summary>Every location (Owner cross-location session).</summary>
    AllLocations,

    /// <summary>One explicit location other than the active one (Owner).</summary>
    SpecificLocation
}

public sealed record OrderListQuery(
    OrderScopeKind Scope, Guid? LocationId, Guid? CashierId, OrderStatus? Status, DateTime? From, DateTime? To, int Page, int PageSize);

public sealed record OrderPage(IReadOnlyList<Order> Orders, int TotalCount);

public sealed record OrderOwner(Guid LocationId, Guid CashierId);
