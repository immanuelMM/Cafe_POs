using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

/// <summary>
/// StaffAssignment has a location query filter (the active location). Every method here deliberately bypasses it with an explicit
/// IgnoreQueryFilters(): auth flows run before an active location exists, and the callers (location/user services) restrict the
/// target user or location to what the current caller is allowed to manage.
/// </summary>
internal sealed class StaffAssignmentRepository(CafeDbContext db) : IStaffAssignmentRepository
{
    private IQueryable<StaffAssignment> All => db.StaffAssignments.IgnoreQueryFilters();

    public Task<StaffAssignment?> GetAsync(Guid userId, Guid locationId, CancellationToken ct) =>
        All.FirstOrDefaultAsync(a => a.UserId == userId && a.LocationId == locationId, ct);

    public async Task<IReadOnlyList<StaffAssignment>> ListAtLocationAsync(Guid userId, Guid locationId, CancellationToken ct) =>
        await All.Where(a => a.UserId == userId && a.LocationId == locationId).ToListAsync(ct);

    public async Task<IReadOnlyList<StaffAssignment>> ListForActiveLocationsAsync(Guid userId, CancellationToken ct) =>
        await All.AsNoTracking().Include(a => a.Location)
            .Where(a => a.UserId == userId && a.Location!.Active).OrderBy(a => a.Location!.Name).ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid userId, Guid locationId, CancellationToken ct) =>
        All.AnyAsync(a => a.UserId == userId && a.LocationId == locationId, ct);

    public Task<bool> AnyAsync(Guid userId, CancellationToken ct) => All.AnyAsync(a => a.UserId == userId, ct);

    public Task<bool> AnyOutsideAsync(Guid userId, Guid locationId, CancellationToken ct) =>
        All.AnyAsync(a => a.UserId == userId && a.LocationId != locationId, ct);

    public void Add(StaffAssignment assignment) => db.StaffAssignments.Add(assignment);

    public void Remove(StaffAssignment assignment) => db.StaffAssignments.Remove(assignment);

    public void RemoveRange(IEnumerable<StaffAssignment> assignments) => db.StaffAssignments.RemoveRange(assignments);
}
