using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Application.Common.Uploads;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Branding;

/// <summary>Business-wide branding (name, colors, logo, tarpaulin) and the editable design-studio documents.</summary>
public sealed class BrandingService(ICurrentUser user, IBrandingRepository branding, IUnitOfWork uow, TimeProvider clock, ILogger<BrandingService> logger)
{
    public const int LogoMaxBytes = 1 * 1024 * 1024;
    public const int TarpaulinMaxBytes = 15 * 1024 * 1024;
    public const int DesignMaxChars = 8 * 1024 * 1024;

    private static BrandingDto ToDto(BrandSettings r)
    {
        var v = r.UpdatedAt.Ticks;
        return new BrandingDto(r.BrandName, r.Tagline, r.PrimaryColor,
            r.LogoContentType is null ? null : $"/api/v1/branding/logo?v={v}",
            r.TarpaulinContentType is null ? null : $"/api/v1/branding/tarpaulin?v={v}",
            r.TarpaulinContentType, r.SecondaryColor, r.TertiaryColor, r.Gradient, r.GradientAngle);
    }

    private static string? NormalizeColor(string? c) => string.IsNullOrWhiteSpace(c) ? null : c.Trim().ToLowerInvariant();

    /// <summary>Loads the tracked singleton row (with image bytes, for writes) or creates it.</summary>
    private async Task<BrandSettings> LoadForWriteAsync(CancellationToken ct)
    {
        var row = await branding.GetForWriteAsync(ct);
        if (row is null) branding.Add(row = new BrandSettings());
        return row;
    }

    /// <summary>Brand settings without the image bytes. Public: the login screen shows it before anyone signs in.</summary>
    public async Task<Result<BrandingDto>> GetAsync(CancellationToken ct) =>
        ToDto(await branding.GetPublicAsync(ct) ?? new BrandSettings());

    /// <summary>Update name, tagline and colors.</summary>
    public async Task<Result<BrandingDto>> UpdateAsync(UpdateBrandingRequest req, CancellationToken ct)
    {
        var name = req.BrandName.Trim();
        if (name.Length == 0) return Error.BadRequest("Invalid brand name", "Brand name is required.");
        var tagline = string.IsNullOrWhiteSpace(req.Tagline) ? null : req.Tagline.Trim();
        var secondary = NormalizeColor(req.SecondaryColor);
        var tertiary = NormalizeColor(req.TertiaryColor);
        if (tertiary is not null && secondary is null)
            return Error.BadRequest("Invalid colors", "Set a secondary color before a tertiary color.");
        if (req.Gradient && secondary is null)
            return Error.BadRequest("Invalid colors", "A gradient needs at least two colors.");

        var row = await LoadForWriteAsync(ct);
        row.BrandName = name;
        row.Tagline = tagline;
        row.PrimaryColor = req.PrimaryColor.ToLowerInvariant();
        row.SecondaryColor = secondary;
        row.TertiaryColor = tertiary;
        row.Gradient = req.Gradient;
        row.GradientAngle = req.GradientAngle;
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Branding updated by {UserId}", user.UserId);
        return ToDto(row);
    }

    // ---- design studio documents ----

    private static Result<string> RequireKind(string kind)
    {
        var k = kind.ToLowerInvariant();
        return BrandDesign.Kinds.Contains(k) ? k : Error.NotFound("Design");
    }

    /// <summary>The saved editable design for "logo" or "banner". 404 when none has been saved yet.</summary>
    public async Task<Result<BrandDesignDto>> GetDesignAsync(string kind, CancellationToken ct)
    {
        var k = RequireKind(kind);
        if (k.IsFailure) return k.Error!;
        var design = await branding.GetDesignAsync(k.Value, ct);
        if (design is null) return Error.NotFound("Design");
        using var doc = JsonDocument.Parse(design.Document);
        return new BrandDesignDto(design.Kind, doc.RootElement.Clone(), design.UpdatedAt);
    }

    /// <summary>Create or replace the editable design. The document must be a JSON object (max 8 MB, embedded images included).</summary>
    public async Task<Result<BrandDesignDto>> SaveDesignAsync(string kind, SaveBrandDesignRequest req, CancellationToken ct)
    {
        var k = RequireKind(kind);
        if (k.IsFailure) return k.Error!;
        if (req.Document.ValueKind != JsonValueKind.Object)
            return Error.BadRequest("Invalid design", "The design document must be a JSON object.");
        var json = req.Document.GetRawText();
        if (json.Length > DesignMaxChars) return Error.BadRequest("Design too large", "The design must be at most 8 MB. Use smaller images.");

        var design = await branding.GetDesignForUpdateAsync(k.Value, ct);
        if (design is null) branding.AddDesign(design = new BrandDesign { Kind = k.Value });
        design.Document = json;
        design.UpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Brand {Kind} design saved by {UserId} ({Chars} chars)", k.Value, user.UserId, json.Length);
        return new BrandDesignDto(k.Value, req.Document, design.UpdatedAt);
    }

    // ---- images ----

    public Task<StoredImage?> GetLogoAsync(CancellationToken ct) => branding.GetLogoAsync(ct);

    public Task<StoredImage?> GetTarpaulinAsync(CancellationToken ct) => branding.GetTarpaulinAsync(ct);

    public async Task<Result<BrandingDto>> PutLogoAsync(UploadedFile? file, CancellationToken ct)
    {
        var upload = await UploadRules.ValidateAsync(file, UploadRules.ImageTypes, LogoMaxBytes, "Logo", ct);
        if (upload.IsFailure) return upload.Error!;
        var row = await LoadForWriteAsync(ct);
        row.LogoData = upload.Value.Data;
        row.LogoContentType = upload.Value.ContentType;
        row.UpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Brand logo uploaded by {UserId} ({Type}, {Bytes} bytes)", user.UserId, upload.Value.ContentType, upload.Value.Data.Length);
        return ToDto(row);
    }

    public async Task<Result<BrandingDto>> DeleteLogoAsync(CancellationToken ct)
    {
        var row = await LoadForWriteAsync(ct);
        row.LogoData = null;
        row.LogoContentType = null;
        row.UpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Brand logo removed by {UserId}", user.UserId);
        return ToDto(row);
    }

    public async Task<Result<BrandingDto>> PutTarpaulinAsync(UploadedFile? file, CancellationToken ct)
    {
        var upload = await UploadRules.ValidateAsync(file, UploadRules.ImageOrPdfTypes, TarpaulinMaxBytes, "Tarpaulin", ct);
        if (upload.IsFailure) return upload.Error!;
        var row = await LoadForWriteAsync(ct);
        row.TarpaulinData = upload.Value.Data;
        row.TarpaulinContentType = upload.Value.ContentType;
        row.UpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Brand tarpaulin uploaded by {UserId} ({Type}, {Bytes} bytes)", user.UserId, upload.Value.ContentType, upload.Value.Data.Length);
        return ToDto(row);
    }

    public async Task<Result<BrandingDto>> DeleteTarpaulinAsync(CancellationToken ct)
    {
        var row = await LoadForWriteAsync(ct);
        row.TarpaulinData = null;
        row.TarpaulinContentType = null;
        row.UpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Brand tarpaulin removed by {UserId}", user.UserId);
        return ToDto(row);
    }
}
