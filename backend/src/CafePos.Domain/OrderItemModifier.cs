namespace CafePos.Domain;

public class OrderItemModifier : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ModifierId { get; set; }
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
}
