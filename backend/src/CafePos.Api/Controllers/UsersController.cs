using CafePos.Api.Http;
using CafePos.Api.Infrastructure;
using CafePos.Application.Common;
using CafePos.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>
/// User management. Owner: all users and locations. Admin: only users assigned to the Admin's active location, who see only
/// the assignments at that location; cannot create or edit Owners; deactivating removes the assignment at their location
/// (global deactivation is Owner only, unless the user has no assignment anywhere else).
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.AdminUp)]
public class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] Guid? locationId, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        (await users.ListAsync(locationId, search, page, pageSize, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct) =>
        (await users.GetAsync(id, ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), 201)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req, CancellationToken ct) =>
        (await users.CreateAsync(req, ct)).ToCreated(u => $"/api/v1/users/{u.Id}");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest req, CancellationToken ct) =>
        (await users.UpdateAsync(id, req, ct)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await users.DeactivateAsync(id, ct)).ToNoContent();

    /// <summary>Owner/Admin sets a user's time clock PIN. Same visibility as PUT /users/{id}; an Admin cannot touch an Owner (403).</summary>
    [HttpPut("{id:guid}/pin")]
    public async Task<IActionResult> SetPin(Guid id, SetPinRequest req, CancellationToken ct) =>
        (await users.SetPinAsync(id, req, ct)).ToNoContent();

    [HttpDelete("{id:guid}/pin")]
    public async Task<IActionResult> ClearPin(Guid id, CancellationToken ct) =>
        (await users.ClearPinAsync(id, ct)).ToNoContent();

    /// <summary>Assign the user to a location with a role (or change the role if already assigned).</summary>
    [HttpPost("{id:guid}/locations")]
    public async Task<ActionResult<UserDto>> Assign(Guid id, AssignmentRequest req, CancellationToken ct) =>
        (await users.AssignAsync(id, req, ct)).ToActionResult();

    [HttpDelete("{id:guid}/locations/{locationId:guid}")]
    public async Task<IActionResult> Unassign(Guid id, Guid locationId, CancellationToken ct) =>
        (await users.UnassignAsync(id, locationId, ct)).ToNoContent();
}
