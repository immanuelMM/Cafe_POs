namespace CafePos.Domain;

/// <summary>Append-only ledger. Never updated or deleted (enforced in CafeDbContext.SaveChanges).</summary>
public class StockMovement : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal QuantityDelta { get; set; }
    public StockMovementReason Reason { get; set; }
    public string? SourceDocumentRef { get; set; }
    public string? Note { get; set; }
    public string? Supplier { get; set; }
    public decimal? UnitCost { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UserId { get; set; }
}
