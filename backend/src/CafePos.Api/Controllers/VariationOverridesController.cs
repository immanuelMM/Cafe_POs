using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

/// <summary>Per-location overrides of a global variation (price, inventory tracking, low-stock threshold).</summary>
[ApiController]
[Route("api/v1/variations/{variationId:guid}/location-overrides")]
[Authorize(Policy = Policies.ManagerUp)]
public class VariationOverridesController : ControllerBase
{
    private readonly CafeDbContext _db;
    private readonly ILocationAccess _access;
    private readonly ILogger<VariationOverridesController> _logger;
    public VariationOverridesController(CafeDbContext db, ILocationAccess access, ILogger<VariationOverridesController> logger) { _db = db; _access = access; _logger = logger; }

    /// <summary>Owner: overrides for every location. Others: the override at their active location (0 or 1 rows).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationOverrideDto>>> List(Guid variationId, CancellationToken ct)
    {
        var variation = await _db.ItemVariations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variationId, ct) ?? throw ApiException.NotFound("Variation");
        var q = _db.ItemVariationLocationOverrides.AsNoTracking().Where(o => o.ItemVariationId == variationId);
        if (!User.IsOwner()) { var active = User.RequireLocationId(); q = q.Where(o => o.LocationId == active); }
        var names = await _db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return (await q.ToListAsync(ct)).OrderBy(o => names.GetValueOrDefault(o.LocationId))
            .Select(o => new LocationOverrideDto(o.ItemVariationId, o.LocationId, names.GetValueOrDefault(o.LocationId, ""), o.PriceOverride,
                o.TrackInventoryOverride, o.LowStockThresholdOverride, o.PriceOverride ?? variation.Price)).ToList();
    }

    /// <summary>Upsert the override for one location (Owner: any; others: own active location). All fields null removes it (204).</summary>
    [HttpPut("{locationId:guid}")]
    [ProducesResponseType(typeof(LocationOverrideDto), 200)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Put(Guid variationId, Guid locationId, UpsertLocationOverrideRequest req, CancellationToken ct)
    {
        await _access.EnsureAsync(User, locationId);
        var variation = await _db.ItemVariations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variationId, ct) ?? throw ApiException.NotFound("Variation");
        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct) ?? throw ApiException.NotFound("Location");

        var row = await _db.ItemVariationLocationOverrides.FirstOrDefaultAsync(o => o.ItemVariationId == variationId && o.LocationId == locationId, ct);
        if (req.PriceOverride is null && req.TrackInventoryOverride is null && req.LowStockThresholdOverride is null)
        {
            if (row is not null) { _db.ItemVariationLocationOverrides.Remove(row); await _db.SaveChangesAsync(ct); }
            return NoContent();
        }
        if (row is null)
        {
            row = new ItemVariationLocationOverride { ItemVariationId = variationId, LocationId = locationId };
            _db.ItemVariationLocationOverrides.Add(row);
        }
        row.PriceOverride = req.PriceOverride;
        row.TrackInventoryOverride = req.TrackInventoryOverride;
        row.LowStockThresholdOverride = req.LowStockThresholdOverride;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Override for variation {VariationId} at {LocationId} set by {UserId}", variationId, locationId, User.RequireUserId());
        return Ok(new LocationOverrideDto(variationId, locationId, location.Name, row.PriceOverride, row.TrackInventoryOverride,
            row.LowStockThresholdOverride, row.PriceOverride ?? variation.Price));
    }
}
