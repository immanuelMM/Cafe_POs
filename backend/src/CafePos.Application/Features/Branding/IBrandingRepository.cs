using CafePos.Application.Common.Uploads;
using CafePos.Domain;

namespace CafePos.Application.Features.Branding;

public interface IBrandingRepository
{
    /// <summary>Brand settings without the image byte columns (read-only), or null when the row does not exist.</summary>
    Task<BrandSettings?> GetPublicAsync(CancellationToken ct);

    /// <summary>The tracked singleton row including image bytes, or null when it does not exist yet.</summary>
    Task<BrandSettings?> GetForWriteAsync(CancellationToken ct);

    void Add(BrandSettings settings);

    Task<StoredImage?> GetLogoAsync(CancellationToken ct);

    Task<StoredImage?> GetTarpaulinAsync(CancellationToken ct);

    Task<BrandDesign?> GetDesignAsync(string kind, CancellationToken ct);

    Task<BrandDesign?> GetDesignForUpdateAsync(string kind, CancellationToken ct);

    void AddDesign(BrandDesign design);
}
