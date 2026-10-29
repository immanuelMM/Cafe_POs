using CafePos.Domain;

namespace CafePos.Application.Features.Locations;

/// <summary>
/// Staff assignments (user x location x role). Every method reads across locations (explicit IgnoreQueryFilters inside the
/// implementation): auth flows run before an active location exists, and the user and location services always restrict
/// themselves to a user or location the caller is allowed to manage.
/// </summary>
public interface IStaffAssignmentRepository
{
    /// <summary>Tracked assignment of one user at one location.</summary>
    Task<StaffAssignment?> GetAsync(Guid userId, Guid locationId, CancellationToken ct);

    /// <summary>Tracked assignments of a user at one location.</summary>
    Task<IReadOnlyList<StaffAssignment>> ListAtLocationAsync(Guid userId, Guid locationId, CancellationToken ct);

    /// <summary>The user's assignments to active locations (location loaded, read-only), by location name.</summary>
    Task<IReadOnlyList<StaffAssignment>> ListForActiveLocationsAsync(Guid userId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid userId, Guid locationId, CancellationToken ct);

    /// <summary>True when the user has an assignment anywhere.</summary>
    Task<bool> AnyAsync(Guid userId, CancellationToken ct);

    /// <summary>True when the user has an assignment at a location other than <paramref name="locationId"/>.</summary>
    Task<bool> AnyOutsideAsync(Guid userId, Guid locationId, CancellationToken ct);

    void Add(StaffAssignment assignment);

    void Remove(StaffAssignment assignment);

    void RemoveRange(IEnumerable<StaffAssignment> assignments);
}
