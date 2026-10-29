using CafePos.Domain;

namespace CafePos.Application.Features.Auth;

public interface IRefreshTokenRepository
{
    /// <summary>Tracked refresh token by the hash of its raw value, or null.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    void Add(RefreshToken token);

    /// <summary>
    /// Revokes the active token with this hash (applied immediately, not via the unit of work). When <paramref name="userId"/> is set
    /// the token must also belong to that user.
    /// </summary>
    Task RevokeByHashAsync(string tokenHash, Guid? userId, DateTime revokedAt, CancellationToken ct);

    /// <summary>Revokes every active refresh token of the user (applied immediately, not via the unit of work).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct);

    /// <summary>Revokes the user's active refresh tokens that are bound to one location.</summary>
    Task RevokeForUserAtLocationAsync(Guid userId, Guid locationId, DateTime revokedAt, CancellationToken ct);
}
