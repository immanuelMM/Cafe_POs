namespace CafePos.Application.Features.Inventory;

public record CreateIngredientRequest
{
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "each";
    public decimal LowStockThreshold { get; init; }
    public decimal InitialQuantity { get; init; }
}

public record UpdateIngredientRequest
{
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "each";
    public decimal LowStockThreshold { get; init; }
    public bool Active { get; init; } = true;
}

public record RestockRequest
{
    public Guid IngredientId { get; init; }
    public decimal Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public string? Supplier { get; init; }
    public string? Note { get; init; }
}

public record AdjustmentRequest
{
    /// <summary>Signed change. Non-zero.</summary>
    public decimal QuantityDelta { get; init; }
    /// <summary>"Adjustment" (default) or "Waste" (waste must be negative).</summary>
    public string Reason { get; init; } = "Adjustment";
    public string Note { get; init; } = "";
}

public record SetRecipeLine
{
    public Guid IngredientId { get; init; }
    public decimal Quantity { get; init; }
}

public record SetRecipeRequest
{
    public List<SetRecipeLine> Lines { get; init; } = new();
}
