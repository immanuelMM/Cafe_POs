namespace CafePos.Application.Features.Orders;

public record OrderLineRequest
{
    public Guid ItemVariationId { get; init; }
    public int Quantity { get; init; } = 1;
    public List<Guid>? ModifierIds { get; init; }
    public string? Note { get; init; }
}

public record CreateOrderRequest
{
    /// <summary>Client-generated order id (UUID). Re-posting the same id returns the existing order.</summary>
    public Guid Id { get; init; }
    public string? Note { get; init; }
    public List<OrderLineRequest> Items { get; init; } = new();
}

public record CompleteOrderRequest
{
    public string PaymentMethod { get; init; } = "cash";
    public decimal? AmountTendered { get; init; }
}
