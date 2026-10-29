using CafePos.Api.Http;
using CafePos.Application.Features.TimeClock;
using CafePos.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>
/// Shared-PIN staff time clock. The kiosk runs inside a signed-in session with an active location; staff pick their name and
/// enter a personal PIN. A wrong PIN is 403 (never 401: the client retries every 401 after a token refresh).
/// </summary>
[ApiController]
[Route("api/v1/timeclock")]
[Authorize]
public class TimeClockController(TimeClockService timeClock) : ControllerBase
{
    [HttpGet("staff")]
    public async Task<ActionResult<List<TimeClockStaffDto>>> Staff(CancellationToken ct) =>
        (await timeClock.StaffAsync(ct)).ToActionResult();

    [HttpPost("punch")]
    public async Task<ActionResult<PunchResultDto>> Punch(PunchRequest req, CancellationToken ct) =>
        (await timeClock.PunchAsync(req, ct)).ToActionResult();

    /// <summary>Sets the caller's own PIN. No active location needed.</summary>
    [HttpPut("my-pin")]
    public async Task<IActionResult> SetMyPin(SetPinRequest req, CancellationToken ct) =>
        (await timeClock.SetMyPinAsync(req.Pin, ct)).ToNoContent();

    [HttpGet("entries")]
    public async Task<ActionResult<List<TimeEntryDto>>> Entries([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId, CancellationToken ct) =>
        (await timeClock.EntriesAsync(from, to, userId, ct)).ToActionResult();
}
