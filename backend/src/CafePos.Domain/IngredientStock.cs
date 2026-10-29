namespace CafePos.Domain;

/// <summary>Running stock level per (location, ingredient). Decremented only via conditional UPDATE alongside a StockMovement.</summary>
public class IngredientStock : ILocationScoped
{
    public Guid LocationId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public Ingredient? Ingredient { get; set; }
}
