namespace CafePos.Domain;

public class RecipeLine
{
    public Guid Id { get; set; }
    public Guid ItemVariationId { get; set; }
    public Guid IngredientId { get; set; }
    /// <summary>Amount consumed per ONE unit sold, in the unit of the ingredient.</summary>
    public decimal Quantity { get; set; }
    public Ingredient? Ingredient { get; set; }
}
