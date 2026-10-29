using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

public static class InventoryMappings
{
    /// <param name="onHand">Null only for the global catalog view (no location, so no stock).</param>
    public static IngredientDto ToDto(this Ingredient i, decimal? onHand) =>
        new(i.Id, i.Name, i.Unit, i.LowStockThreshold, i.Active, onHand, onHand is null ? null : onHand <= i.LowStockThreshold);

    public static StockMovementDto ToDto(this StockMovement m) =>
        new(m.Id, m.IngredientId, m.LocationId, m.QuantityDelta, m.Reason.ToString(), m.SourceDocumentRef, m.Note, m.Supplier, m.UnitCost, m.CreatedAt, m.UserId);
}
