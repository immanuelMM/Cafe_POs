using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class LocationRepository(CafeDbContext db) : ILocationRepository
{
    public async Task<IReadOnlyList<Location>> ListAllAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().Where(l => l.Active).OrderBy(l => l.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Location>> ListActiveAssignedToAsync(Guid userId, CancellationToken ct) =>
        await db.StaffAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == userId && a.Location!.Active).Select(a => a.Location!).OrderBy(l => l.Name).ToListAsync(ct);

    public Task<Location?> GetAsync(Guid id, CancellationToken ct) =>
        db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<Location?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Locations.AnyAsync(l => l.Id == id, ct);

    public Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct) => db.Locations.AnyAsync(l => l.Id == id && l.Active, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    public async Task<IReadOnlyList<StaffMemberDto>> ListStaffAsync(Guid locationId, CancellationToken ct) =>
        await (from a in db.StaffAssignments.IgnoreQueryFilters().AsNoTracking()
               join u in db.Users.AsNoTracking() on a.UserId equals u.Id
               where a.LocationId == locationId
               orderby u.DisplayName
               select new StaffMemberDto(a.UserId, u.Email ?? "", u.DisplayName, a.Role, u.Active)).ToListAsync(ct);

    public void Add(Location location) => db.Locations.Add(location);
}
