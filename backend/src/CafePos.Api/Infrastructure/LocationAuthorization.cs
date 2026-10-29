using System.Security.Claims;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Infrastructure;

public static class Policies
{
    public const string OwnerOnly = "OwnerOnly";
    public const string AdminUp = "AdminUp";
    public const string ManagerUp = "ManagerUp";
    /// <summary>Resource policy: the resource is a LocationId (Guid).</summary>
    public const string LocationAccess = "LocationAccess";

    public static void Register(AuthorizationOptions o)
    {
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); // secure by default
        o.AddPolicy(OwnerOnly, p => p.RequireRole(Roles.Owner));
        o.AddPolicy(AdminUp, p => p.RequireRole(Roles.Admin, Roles.Owner));
        o.AddPolicy(ManagerUp, p => p.RequireRole(Roles.Manager, Roles.Admin, Roles.Owner));
        o.AddPolicy(LocationAccess, p => p.RequireAuthenticatedUser().AddRequirements(new LocationAccessRequirement()));
    }
}

public sealed class LocationAccessRequirement : IAuthorizationRequirement { }

/// <summary>
/// The resource's LocationId must be the caller's active location (token claim, which was only issued for a location the user is
/// assigned to), or the caller must be an Owner. A client-sent locationId is never enough on its own.
/// </summary>
public sealed class LocationAccessHandler : AuthorizationHandler<LocationAccessRequirement, Guid>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LocationAccessRequirement requirement, Guid locationId)
    {
        if (context.User.IsOwner() || context.User.ActiveLocationId() == locationId) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public interface ILocationAccess
{
    /// <summary>Throws 403 unless the caller may act on the given location.</summary>
    Task EnsureAsync(ClaimsPrincipal user, Guid locationId);

    /// <summary>
    /// Resolves which location a read should target. Non-owners: only their active location. Owners: the requested one (an explicit
    /// cross-location read, flagged so callers use IgnoreQueryFilters) or their active one.
    /// </summary>
    Task<LocationTarget> ResolveAsync(ClaimsPrincipal user, Guid? requested, CancellationToken ct);
}

public readonly record struct LocationTarget(Guid LocationId, bool CrossLocation);

public sealed class LocationAccess : ILocationAccess
{
    private readonly IAuthorizationService _auth;
    private readonly CafeDbContext _db;
    public LocationAccess(IAuthorizationService auth, CafeDbContext db) { _auth = auth; _db = db; }

    public async Task EnsureAsync(ClaimsPrincipal user, Guid locationId)
    {
        var result = await _auth.AuthorizeAsync(user, locationId, Policies.LocationAccess);
        if (!result.Succeeded)
            throw ApiException.Forbidden("You do not have access to that location. Switch location first (POST /api/v1/auth/switch-location).");
    }

    public async Task<LocationTarget> ResolveAsync(ClaimsPrincipal user, Guid? requested, CancellationToken ct)
    {
        var active = user.ActiveLocationId();
        if (requested is null || requested == active)
            return new LocationTarget(active ?? throw ApiException.LocationRequired(), false);
        await EnsureAsync(user, requested.Value);
        if (!await _db.Locations.AnyAsync(l => l.Id == requested.Value, ct)) throw ApiException.NotFound("Location");
        return new LocationTarget(requested.Value, true);
    }
}
