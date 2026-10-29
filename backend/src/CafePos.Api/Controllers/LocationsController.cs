using CafePos.Api.Http;
using CafePos.Api.Infrastructure;
using CafePos.Application.Features.Locations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
[Authorize]
public class LocationsController(LocationService locations) : ControllerBase
{
    /// <summary>Owner: every location (including inactive). Everyone else: the active locations they are assigned to.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> List(CancellationToken ct) =>
        (await locations.ListAsync(ct)).ToActionResult();

    /// <summary>The caller's active location (name, address, VAT rate for the POS header and tax display).</summary>
    [HttpGet("current")]
    public async Task<ActionResult<LocationDto>> Current(CancellationToken ct) =>
        (await locations.CurrentAsync(ct)).ToActionResult();

    [HttpPost, Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(LocationDto), 201)]
    public async Task<ActionResult<LocationDto>> Create(CreateLocationRequest req, CancellationToken ct) =>
        (await locations.CreateAsync(req, ct)).ToCreated(l => $"/api/v1/locations/{l.Id}");

    /// <summary>Owner: any location. Admin: own active location only (403 otherwise). Active/StripeTerminalLocationId are Owner only.</summary>
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<LocationDto>> Update(Guid id, UpdateLocationRequest req, CancellationToken ct) =>
        (await locations.UpdateAsync(id, req, ct)).ToActionResult();

    /// <summary>Staff assigned to a location. Owner: any location. Admin: own active location.</summary>
    [HttpGet("{id:guid}/staff"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<IReadOnlyList<StaffMemberDto>>> Staff(Guid id, CancellationToken ct) =>
        (await locations.StaffAsync(id, ct)).ToActionResult();

    /// <summary>
    /// Add an existing user (found by exact email) to this location with a role, or change their role here. This is how an Admin
    /// brings in a user from another branch without ever seeing that user's other assignments. Owner: any location; Admin: own location.
    /// </summary>
    [HttpPost("{id:guid}/staff"), Authorize(Policy = Policies.AdminUp)]
    [ProducesResponseType(typeof(StaffMemberDto), 200)]
    public async Task<ActionResult<StaffMemberDto>> AssignStaff(Guid id, AssignStaffByEmailRequest req, CancellationToken ct) =>
        (await locations.AssignStaffAsync(id, req, ct)).ToActionResult();
}
