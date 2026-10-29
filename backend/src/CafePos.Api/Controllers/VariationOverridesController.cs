using CafePos.Api.Http;
using CafePos.Api.Auth;
using CafePos.Application.Features.VariationOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>Per-location overrides of a global variation (price, inventory tracking, low-stock threshold).</summary>
[ApiController]
[Route("api/v1/variations/{variationId:guid}/location-overrides")]
[Authorize(Policy = Policies.ManagerUp)]
public class VariationOverridesController(VariationOverrideService overrides) : ControllerBase
{
    /// <summary>Owner: overrides for every location. Others: the override at their active location (0 or 1 rows).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationOverrideDto>>> List(Guid variationId, CancellationToken ct) =>
        (await overrides.ListAsync(variationId, ct)).ToActionResult();

    /// <summary>Upsert the override for one location (Owner: any; others: own active location). All fields null removes it (204).</summary>
    [HttpPut("{locationId:guid}")]
    [ProducesResponseType(typeof(LocationOverrideDto), 200)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Put(Guid variationId, Guid locationId, UpsertLocationOverrideRequest req, CancellationToken ct)
    {
        var result = await overrides.PutAsync(variationId, locationId, req, ct);
        if (result.IsFailure) return result.Error!.ToProblem();
        return result.Value is null ? NoContent() : Ok(result.Value);
    }
}
