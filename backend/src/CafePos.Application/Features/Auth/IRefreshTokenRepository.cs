namespace CafePos.Application.Features.Auth;

public interface IRefreshTokenRepository
{
    /// <summary>Revokes every active refresh token of the user (applied immediately, not via the unit of work).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct);

    /// <summary>Revokes the user's active refresh tokens that are bound to one location.</summary>
    Task RevokeForUserAtLocationAsync(Guid userId, Guid locationId, DateTime revokedAt, CancellationToken ct);
}
