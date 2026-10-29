using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Auth;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Users;

/// <summary>
/// User management. Owner: all users and locations. Admin: only users assigned to the Admin's active location, who see only
/// the assignments at that location; cannot create or edit Owners; deactivating removes the assignment at their location
/// (global deactivation is Owner only, unless the user has no assignment anywhere else).
/// </summary>
public sealed class UserService(
    ICurrentUser user,
    IIdentityService identity,
    IUserRepository users,
    IStaffAssignmentRepository assignments,
    ILocationRepository locations,
    IRefreshTokenRepository refreshTokens,
    IPinHasher pins,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<UserService> logger)
{
    /// <summary>Owners see every assignment; Admins only the ones at their own active location (no other-branch names or roles).</summary>
    private Guid? AssignmentScope => user.IsOwner ? null : user.LocationId;

    public async Task<Result<PagedResult<UserDto>>> ListAsync(Guid? locationId, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);
        var scope = locationId;
        var excludeOwners = false;
        if (!user.IsOwner)
        {
            var active = user.RequireLocation();
            if (active.IsFailure) return active.Error!;
            if (locationId is not null && locationId != active.Value) return Error.Forbidden("Admins can only list users of their active location.");
            scope = active.Value;
            excludeOwners = true;
        }
        return await users.ListAsync(new UserListQuery(scope, excludeOwners, ScopeAssignmentsToLocation: !user.IsOwner, search, p, ps), ct);
    }

    public async Task<Result<UserDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var target = await GetManageableAsync(id, ct);
        if (target.IsFailure) return target.Error!;
        return await users.ToDtoAsync(target.Value, AssignmentScope, ct);
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        if (!user.IsOwner && req.IsOwner) return Error.Forbidden("Only an Owner can create an Owner.");
        if (!req.IsOwner && req.Assignments.Count == 0)
            return Error.BadRequest("Assignment required", "A non-Owner user needs at least one location assignment, otherwise they can never log in.");
        if (!user.IsOwner && req.Assignments.Count > 0)
        {
            var active = user.RequireLocation();
            if (active.IsFailure) return active.Error!;
            if (req.Assignments.Any(a => a.LocationId != active.Value)) return Error.Forbidden("Admins can only assign users to their active location.");
        }
        if (req.Assignments.Select(a => a.LocationId).Distinct().Count() != req.Assignments.Count)
            return Error.Unprocessable("Duplicate location", "Each location may appear once.");
        foreach (var a in req.Assignments)
        {
            var valid = await ValidateAssignmentAsync(a, ct);
            if (valid.IsFailure) return valid.Error!;
        }

        var email = req.Email.Trim();
        if (await identity.FindByEmailAsync(email, ct) is not null) return Error.Conflict("Duplicate email", $"'{email}' is already registered.");
        var created = await identity.CreateAsync(email, req.DisplayName.Trim(), req.Password, req.IsOwner, ct);
        if (created.IsFailure) return created.Error!;
        var account = created.Value;

        foreach (var a in req.Assignments)
            assignments.Add(new StaffAssignment { UserId = account.Id, LocationId = a.LocationId, Role = a.Role });
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("User {NewUserId} created by {UserId} (owner={IsOwner}, {Count} assignments)", account.Id, user.UserId, account.IsOwner, req.Assignments.Count);
        return await users.ToDtoAsync(account, AssignmentScope, ct);
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        var found = await GetManageableAsync(id, ct);
        if (found.IsFailure) return found.Error!;
        var target = found.Value;
        if (!user.IsOwner && req.IsOwner is not null) return Error.Forbidden("Only an Owner can change the Owner flag.");
        if (id == user.UserId && (req.Active == false || req.IsOwner == false))
            return Error.Unprocessable("Invalid change", "You cannot deactivate or demote yourself.");
        if (req.IsOwner == false && target.IsOwner && !await assignments.AnyAsync(id, ct))
            return Error.BadRequest("Assignment required", "Assign the user to a location first; a non-Owner without assignments can never log in.");

        target.DisplayName = req.DisplayName.Trim();
        if (req.IsOwner is not null) target.IsOwner = req.IsOwner.Value;
        if (req.Active == false)
        {
            var deactivated = await DeactivateScopedAsync(target, ct); // also persists DisplayName/IsOwner
            if (deactivated.IsFailure) return deactivated.Error!;
        }
        else
        {
            if (req.Active == true && !user.IsOwner)
            {
                var active = user.RequireLocation();
                if (active.IsFailure) return active.Error!;
                if (await assignments.AnyOutsideAsync(id, active.Value, ct))
                    return Error.Forbidden("Only an Owner can reactivate a user who is assigned to other locations.");
            }
            if (req.Active is not null) target.Active = req.Active.Value;
            await identity.UpdateAsync(target, ct);
        }
        logger.LogInformation("User {TargetId} updated by {UserId}", id, user.UserId);
        return await users.ToDtoAsync(target, AssignmentScope, ct);
    }

    /// <summary>
    /// Owner: deactivates the user globally and revokes all their refresh tokens. Admin: removes the user's assignment at the Admin's
    /// location (and revokes sessions bound to it); the account is deactivated globally only if no assignment elsewhere remains.
    /// </summary>
    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var found = await GetManageableAsync(id, ct);
        if (found.IsFailure) return found.Error!;
        if (id == user.UserId) return Error.Unprocessable("Invalid change", "You cannot deactivate yourself.");
        var result = await DeactivateScopedAsync(found.Value, ct);
        if (result.IsFailure) return result;
        logger.LogInformation("User {TargetId} deactivated/unassigned by {UserId}", id, user.UserId);
        return Result.Success();
    }

    /// <summary>Owner/Admin sets a user's time clock PIN. Same visibility as PUT /users/{id}; an Admin cannot touch an Owner (403).</summary>
    public async Task<Result> SetPinAsync(Guid id, SetPinRequest req, CancellationToken ct)
    {
        var target = await GetPinTargetAsync(id, ct);
        if (target.IsFailure) return target.Error!;
        var account = target.Value;
        account.ClockPinHash = pins.Hash(req.Pin);
        account.ClockPinFailedCount = 0;
        account.ClockPinLockedUntil = null;
        await identity.UpdateAsync(account, ct);
        logger.LogInformation("Time clock PIN set for {TargetId} by {UserId}", id, user.UserId);
        return Result.Success();
    }

    public async Task<Result> ClearPinAsync(Guid id, CancellationToken ct)
    {
        var target = await GetPinTargetAsync(id, ct);
        if (target.IsFailure) return target.Error!;
        var account = target.Value;
        account.ClockPinHash = null;
        account.ClockPinFailedCount = 0;
        account.ClockPinLockedUntil = null;
        await identity.UpdateAsync(account, ct);
        logger.LogInformation("Time clock PIN cleared for {TargetId} by {UserId}", id, user.UserId);
        return Result.Success();
    }

    /// <summary>Assign the user to a location with a role (or change the role if already assigned).</summary>
    public async Task<Result<UserDto>> AssignAsync(Guid id, AssignmentRequest req, CancellationToken ct)
    {
        // Location rule first (403 for another location), then visibility (404). An Admin reaches users from other locations
        // only through POST /locations/{id}/staff (by exact email); here they can only change the role of users already at their location.
        var valid = await ValidateAssignmentAsync(req, ct);
        if (valid.IsFailure) return valid.Error!;
        var found = await GetManageableAsync(id, ct);
        if (found.IsFailure) return found.Error!;
        var target = found.Value;
        if (target.IsOwner) return Error.Unprocessable("Owner needs no assignment", "Owners have access to every location.");
        var existing = await assignments.GetAsync(id, req.LocationId, ct);
        if (existing is null) assignments.Add(new StaffAssignment { UserId = id, LocationId = req.LocationId, Role = req.Role });
        else existing.Role = req.Role;
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("User {TargetId} assigned to {LocationId} as {Role} by {UserId}", id, req.LocationId, req.Role, user.UserId);
        return await users.ToDtoAsync(target, AssignmentScope, ct);
    }

    public async Task<Result> UnassignAsync(Guid id, Guid locationId, CancellationToken ct)
    {
        var access = user.EnsureLocationAccess(locationId);
        if (access.IsFailure) return access;
        var row = await assignments.GetAsync(id, locationId, ct);
        if (row is null) return Error.NotFound("Assignment");
        if (id == user.UserId && !user.IsOwner) return Error.Unprocessable("Invalid change", "You cannot remove your own assignment.");
        assignments.Remove(row);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("User {TargetId} unassigned from {LocationId} by {UserId}", id, locationId, user.UserId);
        return Result.Success();
    }

    /// <summary>Loads a user the caller may manage, else 404 (so Admins cannot probe other locations' staff).</summary>
    private async Task<Result<UserAccount>> GetManageableAsync(Guid id, CancellationToken ct)
    {
        var target = await identity.FindByIdAsync(id, ct);
        if (target is null) return Error.NotFound("User");
        if (user.IsOwner) return target;
        var active = user.RequireLocation();
        if (active.IsFailure) return active.Error!;
        if (target.IsOwner || !await assignments.ExistsAsync(id, active.Value, ct)) return Error.NotFound("User");
        return target;
    }

    private async Task<Result<UserAccount>> GetPinTargetAsync(Guid id, CancellationToken ct)
    {
        if (!user.IsOwner)
        {
            var target = await identity.FindByIdAsync(id, ct);
            if (target is null) return Error.NotFound("User");
            if (target.IsOwner) return Error.Forbidden("Only an Owner can change an Owner's PIN.");
        }
        return await GetManageableAsync(id, ct);
    }

    private async Task<Result> ValidateAssignmentAsync(AssignmentRequest a, CancellationToken ct)
    {
        if (!Roles.Assignable.Contains(a.Role)) return Error.Unprocessable("Invalid role", "Role must be Cashier, Manager or Admin.");
        var access = user.EnsureLocationAccess(a.LocationId);
        if (access.IsFailure) return access;
        if (!await locations.ActiveExistsAsync(a.LocationId, ct)) return Error.Unprocessable("Invalid location", "Location does not exist or is inactive.");
        return Result.Success();
    }

    private async Task<Result> DeactivateScopedAsync(UserAccount target, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (user.IsOwner)
        {
            target.Active = false;
            await identity.UpdateAsync(target, ct);
            await refreshTokens.RevokeAllForUserAsync(target.Id, now, ct);
            return Result.Success();
        }
        var active = user.RequireLocation();
        if (active.IsFailure) return active.Error!;
        var mine = await assignments.ListAtLocationAsync(target.Id, active.Value, ct);
        assignments.RemoveRange(mine);
        await uow.SaveChangesAsync(ct);
        await refreshTokens.RevokeForUserAtLocationAsync(target.Id, active.Value, now, ct);
        if (!await assignments.AnyAsync(target.Id, ct))
        {
            target.Active = false; // nothing left anywhere: safe for an Admin to deactivate globally
            await refreshTokens.RevokeAllForUserAsync(target.Id, now, ct);
        }
        await identity.UpdateAsync(target, ct);
        return Result.Success();
    }
}
