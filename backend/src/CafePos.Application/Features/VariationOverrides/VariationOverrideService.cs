using CafePos.Application.Common;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.VariationOverrides;

/// <summary>Per-location overrides of a global variation (price, inventory tracking, low-stock threshold).</summary>
public sealed class VariationOverrideService(
    ICurrentUser user,
    IVariationOverrideRepository overrides,
    ILocationRepository locations,
    IUnitOfWork uow,
    ILogger<VariationOverrideService> logger)
{
    /// <summary>Owner: overrides for every location. Others: the override at their active location (0 or 1 rows).</summary>
    public async Task<Result<IReadOnlyList<LocationOverrideDto>>> ListAsync(Guid variationId, CancellationToken ct)
    {
        var variation = await overrides.GetVariationAsync(variationId, ct);
        if (variation is null) return Error.NotFound("Variation");
        Guid? scope = null;
        if (!user.IsOwner)
        {
            var active = user.RequireLocation();
            if (active.IsFailure) return active.Error!;
            scope = active.Value;
        }
        var names = await locations.GetNamesAsync(ct);
        var rows = await overrides.ListAsync(variationId, scope, ct);
        return rows.OrderBy(o => names.GetValueOrDefault(o.LocationId))
            .Select(o => new LocationOverrideDto(o.ItemVariationId, o.LocationId, names.GetValueOrDefault(o.LocationId, ""), o.PriceOverride,
                o.TrackInventoryOverride, o.LowStockThresholdOverride, o.PriceOverride ?? variation.Price))
            .ToList();
    }

    /// <summary>Upsert the override for one location (Owner: any; others: own active location). All fields null removes it (success, null value).</summary>
    public async Task<Result<LocationOverrideDto?>> PutAsync(Guid variationId, Guid locationId, UpsertLocationOverrideRequest req, CancellationToken ct)
    {
        var access = user.EnsureLocationAccess(locationId);
        if (access.IsFailure) return access.Error!;
        var variation = await overrides.GetVariationAsync(variationId, ct);
        if (variation is null) return Error.NotFound("Variation");
        var location = await locations.GetAsync(locationId, ct);
        if (location is null) return Error.NotFound("Location");

        var row = await overrides.GetAsync(variationId, locationId, ct);
        if (req.PriceOverride is null && req.TrackInventoryOverride is null && req.LowStockThresholdOverride is null)
        {
            if (row is not null)
            {
                overrides.Remove(row);
                await uow.SaveChangesAsync(ct);
            }
            return Result.Success<LocationOverrideDto?>(null);
        }
        if (row is null)
        {
            row = new ItemVariationLocationOverride { ItemVariationId = variationId, LocationId = locationId };
            overrides.Add(row);
        }
        row.PriceOverride = req.PriceOverride;
        row.TrackInventoryOverride = req.TrackInventoryOverride;
        row.LowStockThresholdOverride = req.LowStockThresholdOverride;
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Override for variation {VariationId} at {LocationId} set by {UserId}", variationId, locationId, user.UserId);
        return new LocationOverrideDto(variationId, locationId, location.Name, row.PriceOverride, row.TrackInventoryOverride,
            row.LowStockThresholdOverride, row.PriceOverride ?? variation.Price);
    }
}
