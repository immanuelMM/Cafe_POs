using CafePos.Application.Features.TimeClock;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class TimeClockRepository(CafeDbContext db) : ITimeClockRepository
{
    public async Task<IReadOnlyList<StaffRow>> ListStaffAsync(Guid locationId, CancellationToken ct)
    {
        // StaffAssignments carries a query filter on the active location.
        var rows = await (from a in db.StaffAssignments.AsNoTracking()
                          join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                          where u.Active && !u.IsOwner && a.LocationId == locationId
                          orderby u.DisplayName
                          select new { u.Id, u.DisplayName, a.Role, HasPin = u.ClockPinHash != null }).ToListAsync(ct);
        return rows.Select(r => new StaffRow(r.Id, r.DisplayName, r.Role, r.HasPin)).ToList();
    }

    public Task<bool> IsEligibleAsync(Guid userId, Guid locationId, CancellationToken ct) =>
        (from a in db.StaffAssignments.AsNoTracking()
         join u in db.Users.AsNoTracking() on a.UserId equals u.Id
         where a.UserId == userId && a.LocationId == locationId && u.Active && !u.IsOwner
         select u.Id).AnyAsync(ct);

    public async Task<IReadOnlyList<OpenShift>> ListOpenShiftsAsync(CancellationToken ct) =>
        await db.TimeEntries.AsNoTracking().Where(e => e.ClockOutAt == null)
            .Select(e => new OpenShift(e.UserId, e.ClockInAt)).ToListAsync(ct);

    public Task<TimeEntry?> GetOpenEntryForUpdateAsync(Guid userId, CancellationToken ct) =>
        db.TimeEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.ClockOutAt == null, ct);

    public async Task<IReadOnlyList<TimeEntryRow>> ListEntriesAsync(Guid locationId, DateTime fromUtc, DateTime? toUtc, Guid? userId, CancellationToken ct)
    {
        var q = db.TimeEntries.AsNoTracking().Where(e => e.LocationId == locationId && e.ClockInAt >= fromUtc);
        if (toUtc is { } t) q = q.Where(e => e.ClockInAt < t);
        if (userId is { } uid) q = q.Where(e => e.UserId == uid);

        var rows = await (from e in q
                          join u in db.Users.AsNoTracking() on e.UserId equals u.Id
                          orderby e.ClockInAt descending
                          select new { Entry = e, u.DisplayName }).ToListAsync(ct);
        return rows.Select(r => new TimeEntryRow(r.Entry, r.DisplayName)).ToList();
    }

    public void Add(TimeEntry entry) => db.TimeEntries.Add(entry);
}
