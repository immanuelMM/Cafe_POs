using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Api.Dtos;
using CafePos.Application.Features.Locations;
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

    /// <summary>The role, active location (null = Owner cross-location session) and selectable locations of a session.</summary>
    private sealed record Session(string Role, Location? Active, IReadOnlyList<LocationRoleDto> Locations);

    /// <summary>
    /// Builds a session for the user. Auth flows read StaffAssignments with IgnoreQueryFilters(): they run before (or independent of)
    /// an active location and are always restricted to the user's own id.
    /// </summary>
    private async Task<Session> BuildSessionAsync(ApplicationUser user, Guid? wanted, bool locationRequired, CancellationToken ct)
    {
        if (user.IsOwner)
        {
            var all = await _db.Locations.AsNoTracking().Where(l => l.Active).OrderBy(l => l.Name).ToListAsync(ct);
            Location? active = null;
            if (wanted is { } w)
                active = all.FirstOrDefault(l => l.Id == w) ?? throw new ApiException(403, "No location access", "That location does not exist or is inactive.");
            return new Session(Roles.Owner, active, all.Select(l => new LocationRoleDto(l.Id, l.Name, Roles.Owner)).ToList());
        }

        var assignments = await _db.StaffAssignments.IgnoreQueryFilters().AsNoTracking().Include(a => a.Location)
            .Where(a => a.UserId == user.Id && a.Location!.Active).OrderBy(a => a.Location!.Name).ToListAsync(ct);
        if (assignments.Count == 0)
            throw new ApiException(403, "No location access", "This user is not assigned to any active location.");
        var list = assignments.Select(a => new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role)).ToList();

        StaffAssignment chosen;
        if (wanted is { } id)
            chosen = assignments.FirstOrDefault(a => a.LocationId == id)
                     ?? throw new ApiException(403, "No location access", "This user is not assigned to that location.");
        else if (assignments.Count == 1) chosen = assignments[0];
        else
            throw new ApiException(400, "Location required", locationRequired ? "Send the locationId to switch to." : "This user has several locations; pass locationId.",
                new Dictionary<string, object?> { ["locations"] = assignments.Select(a => new LocationRef(a.LocationId, a.Location!.Name)).ToList() });
        return new Session(chosen.Role, chosen.Location, list);
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

        var session = await BuildSessionAsync(user, req.LocationId, false, ct);
        var response = await IssueAsync(user, session, ct);
        _logger.LogInformation("User {UserId} logged in at location {LocationId} as {Role}", user.Id, session.Active?.Id, session.Role);
        return response;
    }

    /// <summary>Rotate the refresh cookie and return a new access token (same active location as the old session).</summary>
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
        if (user is null || !user.Active) { ClearCookie(); throw Unauthorized401(); }
        Session session;
        try
        {
            // The token's location is preserved; a null location is only valid for an Owner (cross-location session).
            if (token.LocationId is null && !user.IsOwner) throw Unauthorized401();
            session = await BuildSessionAsync(user, token.LocationId, true, ct);
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 403)
        {
            ClearCookie();
            throw Unauthorized401();
        }

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await IssueAsync(user, session, ct);
    }

    /// <summary>
    /// Re-issue the access token (and refresh cookie) for another location the caller may use. Owners may pass null for the
    /// cross-location session. The previous refresh cookie is revoked so a later refresh cannot jump back to the old location.
    /// </summary>
    [HttpPost("switch-location")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> SwitchLocation(SwitchLocationRequest req, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(User.RequireUserId().ToString());
        if (user is null || !user.Active) throw Unauthorized401();
        if (req.LocationId is null && !user.IsOwner)
            throw ApiException.BadRequest("Location required", "Only an Owner can switch to the cross-location session; send a locationId.");

        var session = await BuildSessionAsync(user, req.LocationId, true, ct);

        if (Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
        {
            var hash = TokenService.Hash(raw);
            await _db.RefreshTokens.Where(t => t.TokenHash == hash && t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        }
        var response = await IssueAsync(user, session, ct);
        _logger.LogInformation("User {UserId} switched to location {LocationId} as {Role}", user.Id, session.Active?.Id, session.Role);
        return response;
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

    /// <summary>The authenticated user, role, active location (null for an Owner cross-location session) and assigned locations.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), 200)]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(User.RequireUserId().ToString()) ?? throw Unauthorized401();
        if (!user.Active) throw Unauthorized401();
        var wanted = User.ActiveLocationId();
        if (wanted is null && !user.IsOwner) throw Unauthorized401();
        Session session;
        try { session = await BuildSessionAsync(user, wanted, true, ct); }
        catch (ApiException ex) when (ex.StatusCode is 400 or 403) { throw Unauthorized401(); }
        return BuildUserInfo(user, session);
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, Session session, CancellationToken ct)
    {
        var (access, expires) = _tokens.CreateAccessToken(user, session.Active?.Id, session.Role);
        var (refresh, refreshExpires) = await _tokens.CreateRefreshTokenAsync(user.Id, session.Active?.Id, ct);
        Response.Cookies.Append(RefreshCookie, refresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            Expires = refreshExpires
        });
        return new AuthResponse(access, expires, BuildUserInfo(user, session));
    }

    private static UserInfoDto BuildUserInfo(ApplicationUser user, Session s) =>
        new(user.Id, user.Email ?? "", user.DisplayName, s.Role, user.IsOwner,
            s.Active is null ? null : new LocationRef(s.Active.Id, s.Active.Name), s.Locations);

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/v1/auth" });

    private static ApiException Unauthorized401() => new(401, "Unauthorized", "Invalid credentials or session.");
}
