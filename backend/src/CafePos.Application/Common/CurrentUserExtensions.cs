using CafePos.Domain;

namespace CafePos.Application.Common;

public static class CurrentUserExtensions
{
    public static bool IsManagerUp(this ICurrentUser user) =>
        user.IsInRole(Roles.Manager) || user.IsInRole(Roles.Admin) || user.IsOwner;

    /// <summary>The caller's user id, or 401.</summary>
    public static Result<Guid> RequireUser(this ICurrentUser user) =>
        user.UserId is { } id ? id : Error.Unauthorized();

    /// <summary>The active location, or 400 for an Owner cross-location session / 401 for a token without a location.</summary>
    public static Result<Guid> RequireLocation(this ICurrentUser user) =>
        user.LocationId is { } id
            ? id
            : user.IsOwner
                ? Error.BadRequest("Location required", "This Owner session has no active location. Call POST /api/v1/auth/switch-location first.")
                : Error.Unauthorized("Unauthorized", "Token has no active location.");

    /// <summary>
    /// True when the caller may act on <paramref name="locationId"/>: the active location (the token is only issued for locations the
    /// user is assigned to) or any location for an Owner. A client-sent locationId is never enough on its own.
    /// </summary>
    public static bool CanAccessLocation(this ICurrentUser user, Guid locationId) => user.IsOwner || user.LocationId == locationId;

    public static Error LocationForbidden() =>
        Error.Forbidden("You do not have access to that location. Switch location first (POST /api/v1/auth/switch-location).");

    public static Result EnsureLocationAccess(this ICurrentUser user, Guid locationId) =>
        user.CanAccessLocation(locationId) ? Result.Success() : LocationForbidden();
}
