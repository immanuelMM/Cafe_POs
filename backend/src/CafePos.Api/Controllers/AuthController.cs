using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using CafePos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookie = "cafepos_refresh";

    private readonly UserManager<ApplicationUser> _users;
    private readonly CafeDbContext _db;
    private readonly TokenService _tokens;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager<ApplicationUser> users, CafeDbContext db, TokenService tokens, IOptions<JwtOptions> jwt, ILogger<AuthController> logger)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _jwt = jwt.Value;
        _logger = logger;
    }

    /// <summary>Exchange email + password for a JWT access token (and an HttpOnly refresh cookie).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null || !user.Active)
        {
            _logger.LogWarning("Login failed: unknown or inactive account from {Ip}", HttpContext.Connection.RemoteIpAddress);
            throw Unauthorized401();
        }
        if (await _users.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login blocked: user {UserId} is locked out", user.Id);
            throw new ApiException(423, "Account locked", "Too many failed attempts. Try again later.");
        }
        if (!await _users.CheckPasswordAsync(user, req.Password))
        {
            await _users.AccessFailedAsync(user);
            _logger.LogWarning("Login failed: bad password for user {UserId} from {Ip}", user.Id, HttpContext.Connection.RemoteIpAddress);
            throw Unauthorized401();
        }
        await _users.ResetAccessFailedCountAsync(user);

        var assignments = await _db.StaffAssignments.AsNoTracking().Include(a => a.Location)
            .Where(a => a.UserId == user.Id && a.Location!.Active).ToListAsync(ct);
        if (assignments.Count == 0)
            throw new ApiException(403, "No location access", "This user is not assigned to any active location.");

        StaffAssignment chosen;
        if (req.LocationId is { } wanted)
            chosen = assignments.FirstOrDefault(a => a.LocationId == wanted)
                     ?? throw new ApiException(403, "No location access", "This user is not assigned to that location.");
        else if (assignments.Count == 1) chosen = assignments[0];
        else
            throw new ApiException(400, "Location required", "This user has several locations; pass locationId.",
                new Dictionary<string, object?> { ["locations"] = assignments.Select(a => new LocationRef(a.LocationId, a.Location!.Name)).ToList() });

        var response = await IssueAsync(user, chosen, assignments, ct);
        _logger.LogInformation("User {UserId} logged in at location {LocationId} as {Role}", user.Id, chosen.LocationId, chosen.Role);
        return response;
    }

    /// <summary>Rotate the refresh cookie and return a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrEmpty(raw)) throw Unauthorized401();
        var hash = TokenService.Hash(raw);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) throw Unauthorized401();

        if (token.RevokedAt is not null)
        {
            // A rotated-out token is being replayed: assume theft and kill every session of this user.
            _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", token.UserId);
            await _db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
            ClearCookie();
            throw Unauthorized401();
        }
        if (token.ExpiresAt <= DateTime.UtcNow) { ClearCookie(); throw Unauthorized401(); }

        var user = await _users.FindByIdAsync(token.UserId.ToString());
        var assignments = await _db.StaffAssignments.AsNoTracking().Include(a => a.Location)
            .Where(a => a.UserId == token.UserId && a.Location!.Active).ToListAsync(ct);
        var chosen = assignments.FirstOrDefault(a => a.LocationId == token.LocationId);
        if (user is null || !user.Active || chosen is null) { ClearCookie(); throw Unauthorized401(); }

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await IssueAsync(user, chosen, assignments, ct);
    }

    /// <summary>Revoke the refresh cookie. Always 204.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
        {
            var hash = TokenService.Hash(raw);
            await _db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        }
        ClearCookie();
        return NoContent();
    }

    /// <summary>The authenticated user, role and active location.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), 200)]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var locationId = User.RequireLocationId();
        var user = await _users.FindByIdAsync(userId.ToString()) ?? throw Unauthorized401();
        var assignments = await _db.StaffAssignments.AsNoTracking().Include(a => a.Location).Where(a => a.UserId == userId).ToListAsync(ct);
        var chosen = assignments.FirstOrDefault(a => a.LocationId == locationId) ?? throw Unauthorized401();
        return BuildUserInfo(user, chosen, assignments);
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, StaffAssignment chosen, List<StaffAssignment> all, CancellationToken ct)
    {
        var (access, expires) = _tokens.CreateAccessToken(user, chosen.LocationId, chosen.Role);
        var (refresh, refreshExpires) = await _tokens.CreateRefreshTokenAsync(user.Id, chosen.LocationId, ct);
        Response.Cookies.Append(RefreshCookie, refresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            Expires = refreshExpires
        });
        return new AuthResponse(access, expires, BuildUserInfo(user, chosen, all));
    }

    private static UserInfoDto BuildUserInfo(ApplicationUser user, StaffAssignment chosen, IEnumerable<StaffAssignment> all) =>
        new(user.Id, user.Email ?? "", user.DisplayName, chosen.Role, new LocationRef(chosen.LocationId, chosen.Location!.Name),
            all.Select(a => new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role)).ToList());

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/v1/auth" });

    private static ApiException Unauthorized401() => new(401, "Unauthorized", "Invalid credentials or session.");
}
