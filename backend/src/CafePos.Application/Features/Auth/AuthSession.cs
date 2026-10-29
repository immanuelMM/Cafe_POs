using CafePos.Application.Common;

namespace CafePos.Application.Features.Auth;

/// <summary>A signed-in session: the response body plus the raw refresh token the Api layer puts in the HttpOnly cookie.</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTime RefreshTokenExpiresAt);

/// <summary>
/// Result of an auth operation that issues a session. <see cref="ClearRefreshCookie"/> tells the Api layer to delete the client's
/// refresh cookie because it can no longer be used (revoked, expired, reuse detected, account no longer valid).
/// </summary>
public sealed record AuthOutcome(AuthSession? Session, Error? Error, bool ClearRefreshCookie)
{
    public bool IsSuccess => Error is null;

    public static AuthOutcome Success(AuthSession session) => new(session, null, false);

    public static AuthOutcome Fail(Error error, bool clearCookie = false) => new(null, error, clearCookie);
}
