namespace CafePos.Domain;

/// <summary>
/// Optional per-location tweak of a global variation (plan 4.2). Not ILocationScoped: it is catalog configuration that an Owner
/// may edit for any location, so access is checked explicitly in the controller.
/// </summary>
public class ItemVariationLocationOverride
{
    public Guid ItemVariationId { get; set; }
    public Guid LocationId { get; set; }
    /// <summary>VAT-inclusive price at this location; null inherits ItemVariation.Price.</summary>
    public decimal? PriceOverride { get; set; }
    /// <summary>false = sales of this variation at this location do not consume recipe stock; null inherits (tracked).</summary>
    public bool? TrackInventoryOverride { get; set; }
    /// <summary>Stored for the location; not yet enforced anywhere (ingredient thresholds stay global).</summary>
    public decimal? LowStockThresholdOverride { get; set; }
}
