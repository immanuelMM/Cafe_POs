using CafePos.Application.Features.Auth;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(CafeDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public Task RevokeByHashAsync(string tokenHash, Guid? userId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.TokenHash == tokenHash && t.RevokedAt == null && (userId == null || t.UserId == userId))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);

    public Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);

    public Task RevokeForUserAtLocationAsync(Guid userId, Guid locationId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.LocationId == locationId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);
}
