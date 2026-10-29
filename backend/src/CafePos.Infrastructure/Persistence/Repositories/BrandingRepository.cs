using CafePos.Application.Common.Uploads;
using CafePos.Application.Features.Branding;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class BrandingRepository(CafeDbContext db) : IBrandingRepository
{
    public Task<BrandSettings?> GetPublicAsync(CancellationToken ct) =>
        // Projected into an untracked entity without the image byte columns.
        db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new BrandSettings
            {
                BrandName = b.BrandName, Tagline = b.Tagline, PrimaryColor = b.PrimaryColor, SecondaryColor = b.SecondaryColor,
                TertiaryColor = b.TertiaryColor, Gradient = b.Gradient, GradientAngle = b.GradientAngle,
                LogoContentType = b.LogoContentType, TarpaulinContentType = b.TarpaulinContentType, UpdatedAt = b.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

    public Task<BrandSettings?> GetForWriteAsync(CancellationToken ct) =>
        db.BrandSettings.FirstOrDefaultAsync(b => b.Id == BrandSettings.SingletonId, ct);

    public void Add(BrandSettings settings) => db.BrandSettings.Add(settings);

    public async Task<StoredImage?> GetLogoAsync(CancellationToken ct)
    {
        var row = await db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new { b.LogoData, b.LogoContentType }).FirstOrDefaultAsync(ct);
        return row?.LogoData is { } data && row.LogoContentType is { } type ? new StoredImage(data, type) : null;
    }

    public async Task<StoredImage?> GetTarpaulinAsync(CancellationToken ct)
    {
        var row = await db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new { b.TarpaulinData, b.TarpaulinContentType }).FirstOrDefaultAsync(ct);
        return row?.TarpaulinData is { } data && row.TarpaulinContentType is { } type ? new StoredImage(data, type) : null;
    }

    public Task<BrandDesign?> GetDesignAsync(string kind, CancellationToken ct) =>
        db.BrandDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind, ct);

    public Task<BrandDesign?> GetDesignForUpdateAsync(string kind, CancellationToken ct) =>
        db.BrandDesigns.FirstOrDefaultAsync(x => x.Kind == kind, ct);

    public void AddDesign(BrandDesign design) => db.BrandDesigns.Add(design);
}
