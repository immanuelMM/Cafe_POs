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
/// User management. Owner: all users and locations. Admin: only users assigned to the Admin's active location, and only
/// assignments at that location; cannot create or edit Owners.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.AdminUp)]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly CafeDbContext _db;
    private readonly ILocationAccess _access;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserManager<ApplicationUser> users, CafeDbContext db, ILocationAccess access, ILogger<UsersController> logger)
    {
        _users = users;
        _db = db;
        _access = access;
        _logger = logger;
    }

    // Owner/Admin-gated controller; every action checks the target location or user explicitly.
    private IQueryable<StaffAssignment> Assignments() => _db.StaffAssignments.IgnoreQueryFilters();

    private async Task<UserDto> ToDtoAsync(ApplicationUser u, CancellationToken ct)
    {
        var locs = await Assignments().AsNoTracking().Where(a => a.UserId == u.Id)
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
        if (!User.IsOwner())
        {
            if (req.IsOwner) throw ApiException.Forbidden("Only an Owner can create an Owner.");
            if (req.Assignments.Count == 0) throw ApiException.Unprocessable("Assignment required", "Assign the new user to your location.");
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
        user.DisplayName = req.DisplayName.Trim();
        if (req.Active is not null) user.Active = req.Active.Value;
        if (req.IsOwner is not null) user.IsOwner = req.IsOwner.Value;
        await _users.UpdateAsync(user);
        if (!user.Active) await RevokeSessionsAsync(user.Id, ct);
        _logger.LogInformation("User {TargetId} updated by {UserId}", id, User.RequireUserId());
        return await ToDtoAsync(user, ct);
    }

    /// <summary>Deactivates (never hard deletes) the user and revokes their refresh tokens.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var user = await GetManageableAsync(id, ct);
        if (id == User.RequireUserId()) throw ApiException.Unprocessable("Invalid change", "You cannot deactivate yourself.");
        user.Active = false;
        await _users.UpdateAsync(user);
        await RevokeSessionsAsync(id, ct);
        _logger.LogInformation("User {TargetId} deactivated by {UserId}", id, User.RequireUserId());
        return NoContent();
    }

    /// <summary>Assign the user to a location with a role (or change the role if already assigned).</summary>
    [HttpPost("{id:guid}/locations")]
    public async Task<ActionResult<UserDto>> Assign(Guid id, AssignmentRequest req, CancellationToken ct)
    {
        // An Admin may add a user from another location to their own location, so the user is looked up without the location scope.
        var user = await _users.FindByIdAsync(id.ToString()) ?? throw ApiException.NotFound("User");
        if (user.IsOwner) throw ApiException.Unprocessable("Owner needs no assignment", "Owners have access to every location.");
        await ValidateAssignmentAsync(req, ct);
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

    private Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
}
