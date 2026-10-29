namespace CafePos.Application.Features.Orders;

/// <param name="Created">True when a new order was created (201), false when the client id already existed (200, idempotent).</param>
public sealed record CreateOrderOutcome(OrderDto Order, bool Created);

/// <param name="Replayed">True when the stored response of an earlier completion with the same Idempotency-Key was returned.</param>
public sealed record CompleteOrderOutcome(OrderDto Order, bool Replayed);
