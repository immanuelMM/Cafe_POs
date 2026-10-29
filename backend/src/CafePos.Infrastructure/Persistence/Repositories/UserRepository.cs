using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(CafeDbContext db) : IUserRepository
{
    public async Task<UserDto> ToDtoAsync(UserAccount user, Guid? scopeLocationId, CancellationToken ct)
    {
        var locations = await db.StaffAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == user.Id && (scopeLocationId == null || a.LocationId == scopeLocationId))
            .OrderBy(a => a.Location!.Name)
            .Select(a => new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role))
            .ToListAsync(ct);
        return new UserDto(user.Id, user.Email, user.DisplayName, user.Active, user.IsOwner, locations);
    }

    public async Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking().AsQueryable();
        if (query.ExcludeOwners) q = q.Where(u => !u.IsOwner);
        if (query.LocationId is { } loc)
            q = q.Where(u => db.StaffAssignments.IgnoreQueryFilters().Any(a => a.UserId == u.Id && a.LocationId == loc));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            q = q.Where(u => EF.Functions.ILike(u.DisplayName, like) || EF.Functions.ILike(u.Email!, like));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(u => u.DisplayName).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        Guid? scope = query.ScopeAssignmentsToLocation ? query.LocationId : null;
        var ids = rows.Select(r => r.Id).ToList();
        var roles = await db.StaffAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && (scope == null || a.LocationId == scope))
            .OrderBy(a => a.Location!.Name)
            .Select(a => new { a.UserId, Role = new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role) })
            .ToListAsync(ct);
        var byUser = roles.ToLookup(r => r.UserId, r => r.Role);

        var data = rows.Select(u => new UserDto(u.Id, u.Email ?? "", u.DisplayName, u.Active, u.IsOwner, byUser[u.Id].ToList())).ToList();
        return new PagedResult<UserDto>(data, query.Page, query.PageSize, total);
    }
}
