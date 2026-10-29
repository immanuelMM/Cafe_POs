namespace CafePos.Domain;

public class OrderItem : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ItemVariationId { get; set; }
    public string ItemName { get; set; } = "";
    public string VariationName { get; set; } = "";
    public string Sku { get; set; } = "";
    /// <summary>Variation price plus modifier deltas, VAT-inclusive.</summary>
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public List<OrderItemModifier> Modifiers { get; set; } = new();
}
