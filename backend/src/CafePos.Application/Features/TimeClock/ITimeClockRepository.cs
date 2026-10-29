using CafePos.Domain;

namespace CafePos.Application.Features.TimeClock;

/// <summary>Time entries and the staff roster of the active location (both carry the location query filter).</summary>
public interface ITimeClockRepository
{
    /// <summary>Active, non-owner staff assigned to the location, by display name.</summary>
    Task<IReadOnlyList<StaffRow>> ListStaffAsync(Guid locationId, CancellationToken ct);

    /// <summary>True when the user is active, not an Owner and assigned to the location.</summary>
    Task<bool> IsEligibleAsync(Guid userId, Guid locationId, CancellationToken ct);

    /// <summary>Open shifts (no clock-out) at the active location.</summary>
    Task<IReadOnlyList<OpenShift>> ListOpenShiftsAsync(CancellationToken ct);

    /// <summary>Tracked open entry of a user at the active location, or null.</summary>
    Task<TimeEntry?> GetOpenEntryForUpdateAsync(Guid userId, CancellationToken ct);

    /// <summary>Entries at the location from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive), newest first.</summary>
    Task<IReadOnlyList<TimeEntryRow>> ListEntriesAsync(Guid locationId, DateTime fromUtc, DateTime? toUtc, Guid? userId, CancellationToken ct);

    void Add(TimeEntry entry);
}

public sealed record StaffRow(Guid UserId, string DisplayName, string Role, bool HasPin);

public sealed record OpenShift(Guid UserId, DateTime ClockInAt);

public sealed record TimeEntryRow(TimeEntry Entry, string DisplayName);
