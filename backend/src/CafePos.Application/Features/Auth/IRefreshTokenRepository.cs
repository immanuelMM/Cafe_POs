using CafePos.Domain;

namespace CafePos.Application.Features.Auth;

public interface IRefreshTokenRepository
{
    /// <summary>Tracked refresh token by the hash of its raw value, or null.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    void Add(RefreshToken token);

    /// <summary>
    /// Atomically claims an active refresh token for rotation: revokes it and links it to <paramref name="replacedByHash"/> in one
    /// conditional UPDATE (applied immediately, not via the unit of work). Returns false when the token was no longer active
    /// (already rotated by a concurrent request), which callers treat as reuse. Call inside a transaction with the insert of the replacement.
    /// </summary>
    Task<bool> TryClaimForRotationAsync(string tokenHash, string replacedByHash, DateTime revokedAt, CancellationToken ct);

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
