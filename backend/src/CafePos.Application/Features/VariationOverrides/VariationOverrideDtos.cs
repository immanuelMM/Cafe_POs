namespace CafePos.Application.Features.VariationOverrides;

public record LocationOverrideDto(Guid VariationId, Guid LocationId, string LocationName, decimal? PriceOverride, bool? TrackInventoryOverride,
    decimal? LowStockThresholdOverride, decimal EffectivePrice);

public record UpsertLocationOverrideRequest
{
    public decimal? PriceOverride { get; init; }
    public bool? TrackInventoryOverride { get; init; }
    public decimal? LowStockThresholdOverride { get; init; }
}
