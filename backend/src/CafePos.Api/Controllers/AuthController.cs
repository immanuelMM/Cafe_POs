using CafePos.Api.Http;
using CafePos.Api.RateLimiting;
using CafePos.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    private const string RefreshCookie = "cafepos_refresh";
    private const string RefreshCookiePath = "/api/v1/auth";

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? CurrentRefreshToken => Request.Cookies.TryGetValue(RefreshCookie, out var raw) ? raw : null;

    /// <summary>Exchange email + password for a JWT access token (and an HttpOnly refresh cookie).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct) =>
        Respond(await auth.LoginAsync(req, Ip, ct));

    /// <summary>Rotate the refresh cookie and return a new access token (same active location as the old session).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct) =>
        Respond(await auth.RefreshAsync(CurrentRefreshToken, Ip, ct));

    /// <summary>
    /// Re-issue the access token (and refresh cookie) for another location the caller may use. Owners may pass null for the
    /// cross-location session. The previous refresh cookie is revoked so a later refresh cannot jump back to the old location.
    /// </summary>
    [HttpPost("switch-location")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.SwitchLocation)]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    public async Task<ActionResult<AuthResponse>> SwitchLocation(SwitchLocationRequest req, CancellationToken ct) =>
        Respond(await auth.SwitchLocationAsync(req, CurrentRefreshToken, Ip, ct));

    /// <summary>Revoke the refresh cookie. Always 204.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(CurrentRefreshToken, ct);
        ClearCookie();
        return NoContent();
    }

    /// <summary>The authenticated user, role, active location (null for an Owner cross-location session) and assigned locations.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), 200)]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct) =>
        (await auth.MeAsync(ct)).ToActionResult();

    /// <summary>Sets the refresh cookie and returns the session body, or the problem (clearing a dead cookie when asked to).</summary>
    private ActionResult<AuthResponse> Respond(AuthOutcome outcome)
    {
        if (!outcome.IsSuccess)
        {
            if (outcome.ClearRefreshCookie) ClearCookie();
            return outcome.Error!.ToProblem();
        }
        var session = outcome.Session!;
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = session.RefreshTokenExpiresAt
        });
        return session.Response;
    }

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = RefreshCookiePath });
}
