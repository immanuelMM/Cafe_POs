using CafePos.Application.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(CafeDbContext db) : IRefreshTokenRepository
{
    public Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);

    public Task RevokeForUserAtLocationAsync(Guid userId, Guid locationId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.LocationId == locationId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);
}
