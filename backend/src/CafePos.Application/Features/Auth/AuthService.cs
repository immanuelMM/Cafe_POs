using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Auth;

/// <summary>
/// Login, refresh-token rotation (with reuse detection), location switching and the current-session query. Access tokens are
/// short-lived JWTs; refresh tokens are random, stored hashed, rotated on every use, and bound to the location they were issued for.
/// </summary>
public sealed class AuthService(
    ICurrentUser user,
    IIdentityService identity,
    ILocationRepository locations,
    IStaffAssignmentRepository assignments,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokens,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    /// <summary>The role, active location (null = Owner cross-location session) and selectable locations of a session.</summary>
    private sealed record Session(string Role, Location? Active, IReadOnlyList<LocationRoleDto> Locations);

    private static Error Unauthorized401() => Error.Unauthorized("Unauthorized", "Invalid credentials or session.");

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Builds a session for the user. Auth flows read staff assignments across locations: they run before (or independent of)
    /// an active location and are always restricted to the user's own id.
    /// </summary>
    private async Task<Result<Session>> BuildSessionAsync(UserAccount account, Guid? wanted, bool locationRequired, CancellationToken ct)
    {
        if (account.IsOwner)
        {
            var all = await locations.ListActiveAsync(ct);
            Location? active = null;
            if (wanted is { } w)
            {
                active = all.FirstOrDefault(l => l.Id == w);
                if (active is null) return Error.Forbidden("No location access", "That location does not exist or is inactive.");
            }
            return new Session(Roles.Owner, active, all.Select(l => new LocationRoleDto(l.Id, l.Name, Roles.Owner)).ToList());
        }

        var rows = await assignments.ListForActiveLocationsAsync(account.Id, ct);
        if (rows.Count == 0) return Error.Forbidden("No location access", "This user is not assigned to any active location.");
        var list = rows.Select(a => new LocationRoleDto(a.LocationId, a.Location!.Name, a.Role)).ToList();

        StaffAssignment chosen;
        if (wanted is { } id)
        {
            var match = rows.FirstOrDefault(a => a.LocationId == id);
            if (match is null) return Error.Forbidden("No location access", "This user is not assigned to that location.");
            chosen = match;
        }
        else if (rows.Count == 1) chosen = rows[0];
        else
            return Error.BadRequest("Location required", locationRequired ? "Send the locationId to switch to." : "This user has several locations; pass locationId.",
                new Dictionary<string, object?> { ["locations"] = rows.Select(a => new LocationRef(a.LocationId, a.Location!.Name)).ToList() });
        return new Session(chosen.Role, chosen.Location, list);
    }

    private static UserInfoDto BuildUserInfo(UserAccount account, Session s) =>
        new(account.Id, account.Email, account.DisplayName, s.Role, account.IsOwner,
            s.Active is null ? null : new LocationRef(s.Active.Id, s.Active.Name), s.Locations);

    /// <summary>Creates the access token and a new refresh token; <paramref name="replaces"/> is revoked and linked to the new one (rotation).</summary>
    private async Task<AuthSession> IssueAsync(UserAccount account, Session session, string? ipAddress, RefreshToken? replaces, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(account, session.Active?.Id, session.Role);
        var refresh = tokens.CreateRefreshToken();
        var now = Now;
        refreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = account.Id, LocationId = session.Active?.Id, TokenHash = refresh.Hash,
            CreatedAt = now, ExpiresAt = refresh.ExpiresAt, CreatedByIp = ipAddress
        });
        if (replaces is not null)
        {
            replaces.RevokedAt = now;
            replaces.ReplacedByTokenHash = refresh.Hash;
        }
        await uow.SaveChangesAsync(ct);
        return new AuthSession(new AuthResponse(access.Value, access.ExpiresAt, BuildUserInfo(account, session)), refresh.Raw, refresh.ExpiresAt);
    }

    /// <summary>Exchange email + password for a JWT access token (and a refresh token for the HttpOnly cookie).</summary>
    public async Task<AuthOutcome> LoginAsync(LoginRequest req, string? ipAddress, CancellationToken ct)
    {
        var account = await identity.FindByEmailAsync(req.Email, ct);
        if (account is null || !account.Active)
        {
            logger.LogWarning("Login failed: unknown or inactive account from {Ip}", ipAddress);
            return AuthOutcome.Fail(Unauthorized401());
        }
        if (await identity.IsLockedOutAsync(account, ct))
        {
            logger.LogWarning("Login blocked: user {UserId} is locked out", account.Id);
            return AuthOutcome.Fail(Error.Locked("Account locked", "Too many failed attempts. Try again later."));
        }
        if (!await identity.CheckPasswordAsync(account, req.Password, ct))
        {
            await identity.RecordAccessFailedAsync(account, ct);
            logger.LogWarning("Login failed: bad password for user {UserId} from {Ip}", account.Id, ipAddress);
            return AuthOutcome.Fail(Unauthorized401());
        }
        await identity.ResetAccessFailedAsync(account, ct);

        var session = await BuildSessionAsync(account, req.LocationId, false, ct);
        if (session.IsFailure) return AuthOutcome.Fail(session.Error!);
        var issued = await IssueAsync(account, session.Value, ipAddress, null, ct);
        logger.LogInformation("User {UserId} logged in at location {LocationId} as {Role}", account.Id, session.Value.Active?.Id, session.Value.Role);
        return AuthOutcome.Success(issued);
    }

    /// <summary>Rotate the refresh token and return a new access token (same active location as the old session).</summary>
    public async Task<AuthOutcome> RefreshAsync(string? rawRefreshToken, string? ipAddress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawRefreshToken)) return AuthOutcome.Fail(Unauthorized401());
        var token = await refreshTokens.GetByHashAsync(tokens.Hash(rawRefreshToken), ct);
        if (token is null) return AuthOutcome.Fail(Unauthorized401());

        if (token.RevokedAt is not null)
        {
            // A rotated-out token is being replayed: assume theft and kill every session of this user.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", token.UserId);
            await refreshTokens.RevokeAllForUserAsync(token.UserId, Now, ct);
            return AuthOutcome.Fail(Unauthorized401(), clearCookie: true);
        }
        if (token.ExpiresAt <= Now) return AuthOutcome.Fail(Unauthorized401(), clearCookie: true);

        var account = await identity.FindByIdAsync(token.UserId, ct);
        if (account is null || !account.Active) return AuthOutcome.Fail(Unauthorized401(), clearCookie: true);

        // The token's location is preserved; a null location is only valid for an Owner (cross-location session).
        if (token.LocationId is null && !account.IsOwner) return AuthOutcome.Fail(Unauthorized401());
        var session = await BuildSessionAsync(account, token.LocationId, true, ct);
        if (session.IsFailure)
            return session.Error!.Kind is ErrorKind.BadRequest or ErrorKind.Forbidden
                ? AuthOutcome.Fail(Unauthorized401(), clearCookie: true)
                : AuthOutcome.Fail(session.Error);

        return AuthOutcome.Success(await IssueAsync(account, session.Value, ipAddress, token, ct));
    }

    /// <summary>
    /// Re-issue the access token (and refresh token) for another location the caller may use. Owners may pass null for the
    /// cross-location session. The previous refresh token is revoked so a later refresh cannot jump back to the old location.
    /// </summary>
    public async Task<AuthOutcome> SwitchLocationAsync(SwitchLocationRequest req, string? currentRefreshToken, string? ipAddress, CancellationToken ct)
    {
        var userId = user.RequireUser();
        if (userId.IsFailure) return AuthOutcome.Fail(userId.Error!);
        var account = await identity.FindByIdAsync(userId.Value, ct);
        if (account is null || !account.Active) return AuthOutcome.Fail(Unauthorized401());
        if (req.LocationId is null && !account.IsOwner)
            return AuthOutcome.Fail(Error.BadRequest("Location required", "Only an Owner can switch to the cross-location session; send a locationId."));

        var session = await BuildSessionAsync(account, req.LocationId, true, ct);
        if (session.IsFailure) return AuthOutcome.Fail(session.Error!);

        if (!string.IsNullOrEmpty(currentRefreshToken))
            await refreshTokens.RevokeByHashAsync(tokens.Hash(currentRefreshToken), account.Id, Now, ct);
        var issued = await IssueAsync(account, session.Value, ipAddress, null, ct);
        logger.LogInformation("User {UserId} switched to location {LocationId} as {Role}", account.Id, session.Value.Active?.Id, session.Value.Role);
        return AuthOutcome.Success(issued);
    }

    /// <summary>Revoke the refresh token (when present). The Api layer always answers 204 and clears the cookie.</summary>
    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(rawRefreshToken))
            await refreshTokens.RevokeByHashAsync(tokens.Hash(rawRefreshToken), null, Now, ct);
    }

    /// <summary>The authenticated user, role, active location (null for an Owner cross-location session) and assigned locations.</summary>
    public async Task<Result<UserInfoDto>> MeAsync(CancellationToken ct)
    {
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        var account = await identity.FindByIdAsync(userId.Value, ct);
        if (account is null || !account.Active) return Unauthorized401();
        var wanted = user.LocationId;
        if (wanted is null && !account.IsOwner) return Unauthorized401();
        var session = await BuildSessionAsync(account, wanted, true, ct);
        if (session.IsFailure)
            return session.Error!.Kind is ErrorKind.BadRequest or ErrorKind.Forbidden ? Unauthorized401() : session.Error;
        return BuildUserInfo(account, session.Value);
    }
}
