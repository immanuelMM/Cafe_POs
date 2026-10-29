using CafePos.Domain;

namespace CafePos.Application.Features.Locations;

public interface ILocationRepository
{
    /// <summary>Every location including inactive ones, by name (Owner view).</summary>
    Task<IReadOnlyList<Location>> ListAllAsync(CancellationToken ct);

    /// <summary>Active locations the user is assigned to, by name.</summary>
    Task<IReadOnlyList<Location>> ListActiveAssignedToAsync(Guid userId, CancellationToken ct);

    /// <summary>Read-only lookup.</summary>
    Task<Location?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked lookup for modification.</summary>
    Task<Location?> GetForUpdateAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Names of all locations by id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(CancellationToken ct);

    /// <summary>
    /// Staff assigned to a location, regardless of the caller's active location (an explicit cross-location read; the service has
    /// already checked that the caller is an Owner or an Admin of that location).
    /// </summary>
    Task<IReadOnlyList<StaffMemberDto>> ListStaffAsync(Guid locationId, CancellationToken ct);

    void Add(Location location);
}
