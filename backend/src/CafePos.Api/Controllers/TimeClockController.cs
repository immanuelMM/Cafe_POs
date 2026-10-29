using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

/// <summary>
/// Shared-PIN staff time clock. The kiosk runs inside a signed-in session with an active location; staff pick their name and
/// enter a personal PIN. A wrong PIN is 403 (never 401: the client retries every 401 after a token refresh).
/// </summary>
[ApiController]
[Route("api/v1/timeclock")]
[Authorize]
public class TimeClockController : ControllerBase
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly CafeDbContext _db;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<TimeClockController> _logger;

    public TimeClockController(CafeDbContext db, IPasswordHasher<ApplicationUser> hasher, UserManager<ApplicationUser> users, ILogger<TimeClockController> logger)
    {
        _db = db;
        _hasher = hasher;
        _users = users;
        _logger = logger;
    }

    private static decimal? HoursOf(DateTime start, DateTime? end) =>
        end is null ? null : Math.Round((decimal)(end.Value - start).TotalHours, 2, MidpointRounding.AwayFromZero);

    private static TimeEntryDto ToDto(TimeEntry e, string displayName) =>
        new(e.Id, e.UserId, displayName, e.LocationId, Utc(e.ClockInAt), e.ClockOutAt is { } o ? Utc(o) : null, HoursOf(e.ClockInAt, e.ClockOutAt));

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    [HttpGet("staff")]
    public async Task<ActionResult<List<TimeClockStaffDto>>> Staff(CancellationToken ct)
    {
        var location = User.RequireLocationId();
        // StaffAssignments carries a query filter on the active location.
        var people = await (from a in _db.StaffAssignments.AsNoTracking()
                            join u in _db.Users.AsNoTracking() on a.UserId equals u.Id
                            where u.Active && !u.IsOwner && a.LocationId == location
                            orderby u.DisplayName
                            select new { u.Id, u.DisplayName, a.Role, HasPin = u.ClockPinHash != null }).ToListAsync(ct);
        var open = await _db.TimeEntries.AsNoTracking().Where(e => e.ClockOutAt == null)
            .Select(e => new { e.UserId, e.ClockInAt }).ToListAsync(ct);
        var openByUser = open.GroupBy(o => o.UserId).ToDictionary(g => g.Key, g => g.Min(x => x.ClockInAt));
        return people.Select(p => new TimeClockStaffDto(p.Id, p.DisplayName, p.Role, p.HasPin,
            openByUser.ContainsKey(p.Id), openByUser.TryGetValue(p.Id, out var since) ? Utc(since) : null)).ToList();
    }

    [HttpPost("punch")]
    public async Task<ActionResult<PunchResultDto>> Punch(PunchRequest req, CancellationToken ct)
    {
        var location = User.RequireLocationId();
        var userId = req.UserId!.Value;

        var eligible = await (from a in _db.StaffAssignments.AsNoTracking()
                              join u in _db.Users.AsNoTracking() on a.UserId equals u.Id
                              where a.UserId == userId && a.LocationId == location && u.Active && !u.IsOwner
                              select u.Id).AnyAsync(ct);
        if (!eligible) throw ApiException.NotFound("Staff member");

        var user = await _db.Users.FirstAsync(u => u.Id == userId, ct);
        if (user.ClockPinHash is null) throw ApiException.Unprocessable("PIN not set", "This staff member has no time clock PIN yet.");

        var now = DateTime.UtcNow;
        if (user.ClockPinLockedUntil is { } until && until > now)
        {
            _logger.LogWarning("Time clock punch refused, PIN locked for user {TargetUserId} at {LocationId}", userId, location);
            throw ApiException.Locked("PIN locked", $"Too many wrong PINs. Try again after {until:O}.");
        }

        var verdict = _hasher.VerifyHashedPassword(user, user.ClockPinHash, req.Pin);
        if (verdict == PasswordVerificationResult.Failed)
        {
            user.ClockPinFailedCount = (user.ClockPinLockedUntil is not null ? 0 : user.ClockPinFailedCount) + 1;
            user.ClockPinLockedUntil = null;
            if (user.ClockPinFailedCount >= MaxFailedAttempts)
            {
                user.ClockPinLockedUntil = now + LockDuration;
                user.ClockPinFailedCount = 0;
                _logger.LogWarning("Time clock PIN locked for user {TargetUserId} at {LocationId}", userId, location);
            }
            else
                _logger.LogWarning("Time clock wrong PIN for user {TargetUserId} at {LocationId}", userId, location);
            await _db.SaveChangesAsync(ct);
            throw ApiException.Forbidden("Incorrect PIN");
        }

        user.ClockPinFailedCount = 0;
        user.ClockPinLockedUntil = null;

        var openEntry = await _db.TimeEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.ClockOutAt == null, ct);
        string action;
        if (openEntry is null)
        {
            openEntry = new TimeEntry { Id = Guid.NewGuid(), LocationId = location, UserId = userId, ClockInAt = now };
            _db.TimeEntries.Add(openEntry);
            action = "In";
        }
        else
        {
            openEntry.ClockOutAt = now;
            action = "Out";
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two simultaneous clock-ins: the partial unique index let only one through.
            throw ApiException.Conflict("Punch conflict", "Another punch for this staff member was just recorded. Try again.");
        }
        _logger.LogInformation("Time clock punch {Action} for user {TargetUserId} at {LocationId}", action, userId, location);
        return new PunchResultDto(action, ToDto(openEntry, user.DisplayName));
    }

    /// <summary>Sets the caller's own PIN. No active location needed.</summary>
    [HttpPut("my-pin")]
    public async Task<IActionResult> SetMyPin(SetPinRequest req, CancellationToken ct)
    {
        var id = User.RequireUserId();
        var user = await _users.FindByIdAsync(id.ToString()) ?? throw new ApiException(401, "Unauthorized");
        user.ClockPinHash = _hasher.HashPassword(user, req.Pin);
        user.ClockPinFailedCount = 0;
        user.ClockPinLockedUntil = null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Time clock PIN set by user {UserId}", id);
        return NoContent();
    }

    [HttpGet("entries")]
    public async Task<ActionResult<List<TimeEntryDto>>> Entries([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId, CancellationToken ct)
    {
        var location = User.RequireLocationId();
        var fromUtc = (from ?? DateTimeOffset.UtcNow.AddDays(-7)).UtcDateTime;
        var toUtc = to?.UtcDateTime;

        var q = _db.TimeEntries.AsNoTracking().Where(e => e.LocationId == location && e.ClockInAt >= fromUtc);
        if (toUtc is { } t) q = q.Where(e => e.ClockInAt < t);
        if (!User.IsManagerUp()) q = q.Where(e => e.UserId == User.RequireUserId());
        else if (userId is { } uid) q = q.Where(e => e.UserId == uid);

        var rows = await (from e in q
                          join u in _db.Users.AsNoTracking() on e.UserId equals u.Id
                          orderby e.ClockInAt descending
                          select new { Entry = e, u.DisplayName }).ToListAsync(ct);
        return rows.Select(r => ToDto(r.Entry, r.DisplayName)).ToList();
    }
}
