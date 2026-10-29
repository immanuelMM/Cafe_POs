using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.TimeClock;

/// <summary>
/// Shared-PIN staff time clock. The kiosk runs inside a signed-in session with an active location; staff pick their name and
/// enter a personal PIN. A wrong PIN is 403 (never 401: the client retries every 401 after a token refresh).
/// </summary>
public sealed class TimeClockService(
    ICurrentUser user,
    ITimeClockRepository timeClock,
    IIdentityService identity,
    IPinHasher pins,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<TimeClockService> logger)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static decimal? HoursOf(DateTime start, DateTime? end) =>
        end is null ? null : Math.Round((decimal)(end.Value - start).TotalHours, 2, MidpointRounding.AwayFromZero);

    private static TimeEntryDto ToDto(TimeEntry e, string displayName) =>
        new(e.Id, e.UserId, displayName, e.LocationId, Utc(e.ClockInAt), e.ClockOutAt is { } o ? Utc(o) : null, HoursOf(e.ClockInAt, e.ClockOutAt));

    public async Task<Result<List<TimeClockStaffDto>>> StaffAsync(CancellationToken ct)
    {
        var location = user.RequireLocation();
        if (location.IsFailure) return location.Error!;
        var people = await timeClock.ListStaffAsync(location.Value, ct);
        var open = await timeClock.ListOpenShiftsAsync(ct);
        var openByUser = open.GroupBy(o => o.UserId).ToDictionary(g => g.Key, g => g.Min(x => x.ClockInAt));
        return people.Select(p => new TimeClockStaffDto(p.UserId, p.DisplayName, p.Role, p.HasPin,
            openByUser.ContainsKey(p.UserId), openByUser.TryGetValue(p.UserId, out var since) ? Utc(since) : null)).ToList();
    }

    public async Task<Result<PunchResultDto>> PunchAsync(PunchRequest req, CancellationToken ct)
    {
        var location = user.RequireLocation();
        if (location.IsFailure) return location.Error!;
        var userId = req.UserId!.Value;

        if (!await timeClock.IsEligibleAsync(userId, location.Value, ct)) return Error.NotFound("Staff member");

        var account = await identity.FindByIdAsync(userId, ct);
        if (account is null) return Error.NotFound("Staff member");
        if (account.ClockPinHash is null) return Error.Unprocessable("PIN not set", "This staff member has no time clock PIN yet.");

        var now = clock.GetUtcNow().UtcDateTime;
        if (account.ClockPinLockedUntil is { } until && until > now)
        {
            logger.LogWarning("Time clock punch refused, PIN locked for user {TargetUserId} at {LocationId}", userId, location.Value);
            return Error.Locked("PIN locked", $"Too many wrong PINs. Try again after {until:O}.");
        }

        if (!pins.Verify(account.ClockPinHash, req.Pin))
        {
            account.ClockPinFailedCount = (account.ClockPinLockedUntil is not null ? 0 : account.ClockPinFailedCount) + 1;
            account.ClockPinLockedUntil = null;
            if (account.ClockPinFailedCount >= MaxFailedAttempts)
            {
                account.ClockPinLockedUntil = now + LockDuration;
                account.ClockPinFailedCount = 0;
                logger.LogWarning("Time clock PIN locked for user {TargetUserId} at {LocationId}", userId, location.Value);
            }
            else
                logger.LogWarning("Time clock wrong PIN for user {TargetUserId} at {LocationId}", userId, location.Value);
            identity.ApplyChanges(account);
            await uow.SaveChangesAsync(ct);
            return Error.Forbidden("Incorrect PIN");
        }

        account.ClockPinFailedCount = 0;
        account.ClockPinLockedUntil = null;
        identity.ApplyChanges(account);

        var entry = await timeClock.GetOpenEntryForUpdateAsync(userId, ct);
        string action;
        if (entry is null)
        {
            entry = new TimeEntry { Id = Guid.NewGuid(), LocationId = location.Value, UserId = userId, ClockInAt = now };
            timeClock.Add(entry);
            action = "In";
        }
        else
        {
            entry.ClockOutAt = now;
            action = "Out";
        }
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Two simultaneous clock-ins: the partial unique index let only one through.
            return Error.Conflict("Punch conflict", "Another punch for this staff member was just recorded. Try again.");
        }
        logger.LogInformation("Time clock punch {Action} for user {TargetUserId} at {LocationId}", action, userId, location.Value);
        return new PunchResultDto(action, ToDto(entry, account.DisplayName));
    }

    /// <summary>Sets the caller's own PIN. No active location needed.</summary>
    public async Task<Result> SetMyPinAsync(string pin, CancellationToken ct)
    {
        var id = user.RequireUser();
        if (id.IsFailure) return id.Error!;
        var account = await identity.FindByIdAsync(id.Value, ct);
        if (account is null) return Error.Unauthorized();
        account.ClockPinHash = pins.Hash(pin);
        account.ClockPinFailedCount = 0;
        account.ClockPinLockedUntil = null;
        identity.ApplyChanges(account);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Time clock PIN set by user {UserId}", id.Value);
        return Result.Success();
    }

    public async Task<Result<List<TimeEntryDto>>> EntriesAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? userId, CancellationToken ct)
    {
        var location = user.RequireLocation();
        if (location.IsFailure) return location.Error!;
        var fromUtc = (from ?? clock.GetUtcNow().AddDays(-7)).UtcDateTime;
        var toUtc = to?.UtcDateTime;

        Guid? filter;
        if (!user.IsManagerUp())
        {
            var self = user.RequireUser();
            if (self.IsFailure) return self.Error!;
            filter = self.Value;
        }
        else filter = userId;

        var rows = await timeClock.ListEntriesAsync(location.Value, fromUtc, toUtc, filter, ct);
        return rows.Select(r => ToDto(r.Entry, r.DisplayName)).ToList();
    }
}
