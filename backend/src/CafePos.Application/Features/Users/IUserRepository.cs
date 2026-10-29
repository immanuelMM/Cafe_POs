using CafePos.Application.Common;
using CafePos.Application.Common.Identity;

namespace CafePos.Application.Features.Users;

/// <summary>Read models over users and their location assignments (the accounts themselves are changed through IIdentityService).</summary>
public interface IUserRepository
{
    /// <summary>
    /// The user with their assignments. <paramref name="scopeLocationId"/> limits the assignments to one location (Admins see only
    /// the assignments at their own location); null lists all of them (Owner).
    /// </summary>
    Task<UserDto> ToDtoAsync(UserAccount user, Guid? scopeLocationId, CancellationToken ct);

    Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct);
}

/// <param name="LocationId">Only users assigned to this location; null for every user.</param>
/// <param name="ExcludeOwners">Hide Owner accounts (Admin callers).</param>
/// <param name="ScopeAssignmentsToLocation">Show only the assignments at <paramref name="LocationId"/> in each row.</param>
public sealed record UserListQuery(Guid? LocationId, bool ExcludeOwners, bool ScopeAssignmentsToLocation, string? Search, int Page, int PageSize);
