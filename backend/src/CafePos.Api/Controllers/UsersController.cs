using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

/// <summary>
/// User management. Owner: all users and locations. Admin: only users assigned to the Admin's active location, who see only
/// the assignments at that location; cannot create or edit Owners; deactivating removes the assignment at their location
/// (global deactivation is Owner only, unless the user has no assignment anywhere else).
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.AdminUp)]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly CafeDbContext _db;
    private readonly ILocationAccess _access;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserManager<ApplicationUser> users, CafeDbContext db, ILocationAccess access, IPasswordHasher<ApplicationUser> hasher, ILogger<UsersController> logger)
    {
        _users = users;
        _db = db;
        _access = access;
        _hasher = hasher;
        _logger = logger;
    }

    // Owner/Admin-gated controller; every action checks the target location or user explicitly.
    private IQueryable<StaffAssignment> Assignments() => _db.StaffAssignments.IgnoreQueryFilters();

    /// <summary>Owners see every assignment; Admins only the ones at their own active location (no other-branch names or roles).</summary>
    private async Task<UserDto> ToDtoAsync(ApplicationUser u, CancellationToken ct)
    {
        var scope = User.IsOwner() ? null : User.ActiveLocationId();
        var locs = await Assignments().AsNoTracking().Where(a => a.UserId == u.Id && (scope == null || a.LocationId == scope))
            .OrderBy(a => a.Location!.Name).Select(a => new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role)).ToListAsync(ct);
        return new UserDto(u.Id, u.Email ?? "", u.DisplayName, u.Active, u.IsOwner, locs);
    }

    /// <summary>Loads a user the caller may manage, else 404 (so Admins cannot probe other locations' staff).</summary>
    private async Task<ApplicationUser> GetManageableAsync(Guid id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString()) ?? throw ApiException.NotFound("User");
        if (User.IsOwner()) return user;
        var active = User.RequireLocationId();
        if (user.IsOwner || !await Assignments().AnyAsync(a => a.UserId == id && a.LocationId == active, ct)) throw ApiException.NotFound("User");
        return user;
    }

    private async Task ValidateAssignmentAsync(AssignmentRequest a, CancellationToken ct)
    {
        if (!Roles.Assignable.Contains(a.Role)) throw ApiException.Unprocessable("Invalid role", "Role must be Cashier, Manager or Admin.");
        await _access.EnsureAsync(User, a.LocationId);
        if (!await _db.Locations.AnyAsync(l => l.Id == a.LocationId && l.Active, ct)) throw ApiException.Unprocessable("Invalid location", "Location does not exist or is inactive.");
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] Guid? locationId, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);
        var q = _db.Users.AsNoTracking().AsQueryable();
        Guid? scope = locationId;
        if (!User.IsOwner())
        {
            var active = User.RequireLocationId();
            if (locationId is not null && locationId != active) throw ApiException.Forbidden("Admins can only list users of their active location.");
            scope = active;
            q = q.Where(u => !u.IsOwner);
        }
        if (scope is { } loc)
            q = q.Where(u => _db.StaffAssignments.IgnoreQueryFilters().Any(a => a.UserId == u.Id && a.LocationId == loc));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search.Trim()}%";
            q = q.Where(u => EF.Functions.ILike(u.DisplayName, like) || EF.Functions.ILike(u.Email!, like));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(u => u.DisplayName).Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        var data = new List<UserDto>();
        foreach (var u in rows) data.Add(await ToDtoAsync(u, ct));
        return new PagedResult<UserDto>(data, p, ps, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct) => await ToDtoAsync(await GetManageableAsync(id, ct), ct);

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), 201)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req, CancellationToken ct)
    {
        if (!User.IsOwner() && req.IsOwner) throw ApiException.Forbidden("Only an Owner can create an Owner.");
        if (!req.IsOwner && req.Assignments.Count == 0)
            throw ApiException.BadRequest("Assignment required", "A non-Owner user needs at least one location assignment, otherwise they can never log in.");
        if (!User.IsOwner())
        {
            if (req.Assignments.Any(a => a.LocationId != User.RequireLocationId())) throw ApiException.Forbidden("Admins can only assign users to their active location.");
        }
        if (req.Assignments.Select(a => a.LocationId).Distinct().Count() != req.Assignments.Count)
            throw ApiException.Unprocessable("Duplicate location", "Each location may appear once.");
        foreach (var a in req.Assignments) await ValidateAssignmentAsync(a, ct);

        var email = req.Email.Trim();
        if (await _users.FindByEmailAsync(email) is not null) throw ApiException.Conflict("Duplicate email", $"'{email}' is already registered.");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = req.DisplayName.Trim(), IsOwner = req.IsOwner };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(e.Code.Contains("Password") ? "password" : "email", e.Description);
            return ValidationProblem(ModelState);
        }
        foreach (var a in req.Assignments) _db.StaffAssignments.Add(new StaffAssignment { UserId = user.Id, LocationId = a.LocationId, Role = a.Role });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {NewUserId} created by {UserId} (owner={IsOwner}, {Count} assignments)", user.Id, User.RequireUserId(), user.IsOwner, req.Assignments.Count);
        return Created($"/api/v1/users/{user.Id}", await ToDtoAsync(user, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await GetManageableAsync(id, ct);
        if (!User.IsOwner() && req.IsOwner is not null) throw ApiException.Forbidden("Only an Owner can change the Owner flag.");
        if (id == User.RequireUserId() && (req.Active == false || req.IsOwner == false))
            throw ApiException.Unprocessable("Invalid change", "You cannot deactivate or demote yourself.");
        if (req.IsOwner == false && user.IsOwner && !await Assignments().AnyAsync(a => a.UserId == id, ct))
            throw ApiException.BadRequest("Assignment required", "Assign the user to a location first; a non-Owner without assignments can never log in.");

        user.DisplayName = req.DisplayName.Trim();
        if (req.IsOwner is not null) user.IsOwner = req.IsOwner.Value;
        if (req.Active == false)
            await DeactivateScopedAsync(user, ct); // also persists DisplayName/IsOwner
        else
        {
            if (req.Active == true && !User.IsOwner() && await Assignments().AnyAsync(a => a.UserId == id && a.LocationId != User.RequireLocationId(), ct))
                throw ApiException.Forbidden("Only an Owner can reactivate a user who is assigned to other locations.");
            if (req.Active is not null) user.Active = req.Active.Value;
            await _users.UpdateAsync(user);
        }
        _logger.LogInformation("User {TargetId} updated by {UserId}", id, User.RequireUserId());
        return await ToDtoAsync(user, ct);
    }

    /// <summary>
    /// Owner: deactivates the user globally and revokes all their refresh tokens. Admin: removes the user's assignment at the Admin's
    /// location (and revokes sessions bound to it); the account is deactivated globally only if no assignment elsewhere remains.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var user = await GetManageableAsync(id, ct);
        if (id == User.RequireUserId()) throw ApiException.Unprocessable("Invalid change", "You cannot deactivate yourself.");
        await DeactivateScopedAsync(user, ct);
        _logger.LogInformation("User {TargetId} deactivated/unassigned by {UserId}", id, User.RequireUserId());
        return NoContent();
    }

    /// <summary>Owner/Admin sets a user's time clock PIN. Same visibility as PUT /users/{id}; an Admin cannot touch an Owner (403).</summary>
    [HttpPut("{id:guid}/pin")]
    public async Task<IActionResult> SetPin(Guid id, SetPinRequest req, CancellationToken ct)
    {
        var user = await GetPinTargetAsync(id, ct);
        user.ClockPinHash = _hasher.HashPassword(user, req.Pin);
        user.ClockPinFailedCount = 0;
        user.ClockPinLockedUntil = null;
        await _users.UpdateAsync(user);
        _logger.LogInformation("Time clock PIN set for {TargetId} by {UserId}", id, User.RequireUserId());
        return NoContent();
    }

    [HttpDelete("{id:guid}/pin")]
    public async Task<IActionResult> ClearPin(Guid id, CancellationToken ct)
    {
        var user = await GetPinTargetAsync(id, ct);
        user.ClockPinHash = null;
        user.ClockPinFailedCount = 0;
        user.ClockPinLockedUntil = null;
        await _users.UpdateAsync(user);
        _logger.LogInformation("Time clock PIN cleared for {TargetId} by {UserId}", id, User.RequireUserId());
        return NoContent();
    }

    private async Task<ApplicationUser> GetPinTargetAsync(Guid id, CancellationToken ct)
    {
        if (!User.IsOwner())
        {
            var target = await _users.FindByIdAsync(id.ToString()) ?? throw ApiException.NotFound("User");
            if (target.IsOwner) throw ApiException.Forbidden("Only an Owner can change an Owner's PIN.");
        }
        return await GetManageableAsync(id, ct);
    }

    /// <summary>Assign the user to a location with a role (or change the role if already assigned).</summary>
    [HttpPost("{id:guid}/locations")]
    public async Task<ActionResult<UserDto>> Assign(Guid id, AssignmentRequest req, CancellationToken ct)
    {
        // Location rule first (403 for another location), then visibility (404). An Admin reaches users from other locations
        // only through POST /locations/{id}/staff (by exact email); here they can only change the role of users already at their location.
        await ValidateAssignmentAsync(req, ct);
        var user = await GetManageableAsync(id, ct);
        if (user.IsOwner) throw ApiException.Unprocessable("Owner needs no assignment", "Owners have access to every location.");
        var existing = await Assignments().FirstOrDefaultAsync(a => a.UserId == id && a.LocationId == req.LocationId, ct);
        if (existing is null) _db.StaffAssignments.Add(new StaffAssignment { UserId = id, LocationId = req.LocationId, Role = req.Role });
        else existing.Role = req.Role;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {TargetId} assigned to {LocationId} as {Role} by {UserId}", id, req.LocationId, req.Role, User.RequireUserId());
        return await ToDtoAsync(user, ct);
    }

    [HttpDelete("{id:guid}/locations/{locationId:guid}")]
    public async Task<IActionResult> Unassign(Guid id, Guid locationId, CancellationToken ct)
    {
        await _access.EnsureAsync(User, locationId);
        var row = await Assignments().FirstOrDefaultAsync(a => a.UserId == id && a.LocationId == locationId, ct) ?? throw ApiException.NotFound("Assignment");
        if (id == User.RequireUserId() && !User.IsOwner()) throw ApiException.Unprocessable("Invalid change", "You cannot remove your own assignment.");
        _db.StaffAssignments.Remove(row);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {TargetId} unassigned from {LocationId} by {UserId}", id, locationId, User.RequireUserId());
        return NoContent();
    }

    private async Task DeactivateScopedAsync(ApplicationUser user, CancellationToken ct)
    {
        if (User.IsOwner())
        {
            user.Active = false;
            await _users.UpdateAsync(user);
            await RevokeSessionsAsync(user.Id, ct);
            return;
        }
        var active = User.RequireLocationId();
        var mine = await Assignments().Where(a => a.UserId == user.Id && a.LocationId == active).ToListAsync(ct);
        _db.StaffAssignments.RemoveRange(mine);
        await _db.SaveChangesAsync(ct);
        await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.LocationId == active && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        if (!await Assignments().AnyAsync(a => a.UserId == user.Id, ct))
        {
            user.Active = false; // nothing left anywhere: safe for an Admin to deactivate globally
            await RevokeSessionsAsync(user.Id, ct);
        }
        await _users.UpdateAsync(user);
    }

    private Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
}
