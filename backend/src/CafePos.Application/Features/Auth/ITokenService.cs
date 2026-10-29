using CafePos.Application.Common.Identity;

namespace CafePos.Application.Features.Auth;

/// <summary>Issues access tokens (JWT) and refresh tokens. Implemented in Infrastructure.</summary>
public interface ITokenService
{
    /// <summary>A signed access token for the user. <paramref name="locationId"/> is null for an Owner's cross-location session.</summary>
    AccessToken CreateAccessToken(UserAccount user, Guid? locationId, string role);

    /// <summary>A new random refresh token: the raw value goes to the client, only its hash is stored.</summary>
    NewRefreshToken CreateRefreshToken();

    /// <summary>The stored form (SHA-256, hex) of a raw refresh token.</summary>
    string Hash(string rawRefreshToken);
}

public sealed record AccessToken(string Value, DateTime ExpiresAt);

public sealed record NewRefreshToken(string Raw, string Hash, DateTime ExpiresAt);
