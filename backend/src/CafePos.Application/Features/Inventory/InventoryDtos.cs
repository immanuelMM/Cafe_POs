namespace CafePos.Application.Features.Inventory;

/// <summary>QuantityOnHand/IsLowStock are null only for the global catalog view of an Owner cross-location session (no location, no stock).</summary>
public record IngredientDto(Guid Id, string Name, string Unit, decimal LowStockThreshold, bool Active, decimal? QuantityOnHand, bool? IsLowStock);

public record StockMovementDto(Guid Id, Guid IngredientId, Guid LocationId, decimal QuantityDelta, string Reason, string? SourceDocumentRef,
    string? Note, string? Supplier, decimal? UnitCost, DateTime CreatedAt, Guid? UserId);

public record RecipeLineDto(Guid IngredientId, string IngredientName, string Unit, decimal Quantity);

public record RecipeDto(Guid VariationId, IReadOnlyList<RecipeLineDto> Lines);
