using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/branding")]
[Authorize]
public class BrandingController : ControllerBase
{
    private const int LogoMaxBytes = 1 * 1024 * 1024;
    private const int TarpaulinMaxBytes = 15 * 1024 * 1024;
    // Headroom above the file limit so an oversize file is answered with a 400, not a transport-level 413.
    private const long TarpaulinRequestLimit = 20L * 1024 * 1024;
    private const long LogoRequestLimit = 3L * 1024 * 1024;

    private static readonly string[] LogoTypes = Uploads.ImageTypes;
    private static readonly string[] TarpaulinTypes = Uploads.ImageOrPdfTypes;

    private readonly CafeDbContext _db;
    private readonly ILogger<BrandingController> _logger;
    public BrandingController(CafeDbContext db, ILogger<BrandingController> logger) { _db = db; _logger = logger; }

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
        var row = await _db.BrandSettings.FirstOrDefaultAsync(b => b.Id == BrandSettings.SingletonId, ct);
        if (row is null) _db.BrandSettings.Add(row = new BrandSettings());
        return row;
    }

    /// <summary>Brand settings without the image bytes. Anonymous: the login screen shows it before anyone signs in.</summary>
    [HttpGet, AllowAnonymous]
    public async Task<ActionResult<BrandingDto>> Get(CancellationToken ct)
    {
        // Projected into an untracked entity without the image byte columns.
        var row = await _db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new BrandSettings
            {
                BrandName = b.BrandName, Tagline = b.Tagline, PrimaryColor = b.PrimaryColor, SecondaryColor = b.SecondaryColor,
                TertiaryColor = b.TertiaryColor, Gradient = b.Gradient, GradientAngle = b.GradientAngle,
                LogoContentType = b.LogoContentType, TarpaulinContentType = b.TarpaulinContentType, UpdatedAt = b.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);
        return ToDto(row ?? new BrandSettings());
    }

    /// <summary>Update name, tagline and colors. Business-wide (every location), so Owner or Admin.</summary>
    [HttpPut, Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> Update(UpdateBrandingRequest req, CancellationToken ct)
    {
        var name = req.BrandName.Trim();
        if (name.Length == 0) throw ApiException.BadRequest("Invalid brand name", "Brand name is required.");
        var tagline = string.IsNullOrWhiteSpace(req.Tagline) ? null : req.Tagline.Trim();
        var secondary = NormalizeColor(req.SecondaryColor);
        var tertiary = NormalizeColor(req.TertiaryColor);
        if (tertiary is not null && secondary is null)
            throw ApiException.BadRequest("Invalid colors", "Set a secondary color before a tertiary color.");
        if (req.Gradient && secondary is null)
            throw ApiException.BadRequest("Invalid colors", "A gradient needs at least two colors.");

        var row = await _db.BrandSettings.FirstOrDefaultAsync(b => b.Id == BrandSettings.SingletonId, ct);
        if (row is null) _db.BrandSettings.Add(row = new BrandSettings());
        row.BrandName = name;
        row.Tagline = tagline;
        row.PrimaryColor = req.PrimaryColor.ToLowerInvariant();
        row.SecondaryColor = secondary;
        row.TertiaryColor = tertiary;
        row.Gradient = req.Gradient;
        row.GradientAngle = req.GradientAngle;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Branding updated by {UserId}", User.RequireUserId());
        return ToDto(row);
    }

    // ---- design studio documents ----
    private const int DesignMaxChars = 8 * 1024 * 1024;

    private static string RequireKind(string kind)
    {
        var k = kind.ToLowerInvariant();
        return BrandDesign.Kinds.Contains(k) ? k : throw ApiException.NotFound("Design");
    }

    /// <summary>The saved editable design for "logo" or "banner". 404 when none has been saved yet.</summary>
    [HttpGet("designs/{kind}"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandDesignDto>> GetDesign(string kind, CancellationToken ct)
    {
        var k = RequireKind(kind);
        var d = await _db.BrandDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == k, ct) ?? throw ApiException.NotFound("Design");
        using var doc = System.Text.Json.JsonDocument.Parse(d.Document);
        return new BrandDesignDto(d.Kind, doc.RootElement.Clone(), d.UpdatedAt);
    }

    /// <summary>Create or replace the editable design. The document must be a JSON object (max 8 MB, embedded images included).</summary>
    [HttpPut("designs/{kind}"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(DesignMaxChars + 1024 * 1024)]
    public async Task<ActionResult<BrandDesignDto>> SaveDesign(string kind, SaveBrandDesignRequest req, CancellationToken ct)
    {
        var k = RequireKind(kind);
        if (req.Document.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw ApiException.BadRequest("Invalid design", "The design document must be a JSON object.");
        var json = req.Document.GetRawText();
        if (json.Length > DesignMaxChars) throw ApiException.BadRequest("Design too large", "The design must be at most 8 MB. Use smaller images.");

        var d = await _db.BrandDesigns.FirstOrDefaultAsync(x => x.Kind == k, ct);
        if (d is null) _db.BrandDesigns.Add(d = new BrandDesign { Kind = k });
        d.Document = json;
        d.UpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brand {Kind} design saved by {UserId} ({Chars} chars)", k, User.RequireUserId(), json.Length);
        return new BrandDesignDto(k, req.Document, d.UpdatedAt);
    }

    [HttpGet("logo"), AllowAnonymous]
    public async Task<IActionResult> GetLogo(CancellationToken ct)
    {
        var row = await _db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new { b.LogoData, b.LogoContentType }).FirstOrDefaultAsync(ct);
        return Image(row?.LogoData, row?.LogoContentType);
    }

    [HttpPut("logo"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(LogoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = LogoRequestLimit)]
    public async Task<ActionResult<BrandingDto>> PutLogo([FromForm] UploadFileRequest req, CancellationToken ct)
    {
        var (data, type) = await ReadUploadAsync(req.File, LogoTypes, LogoMaxBytes, "Logo", ct);
        var row = await LoadForWriteAsync(ct);
        row.LogoData = data;
        row.LogoContentType = type;
        row.UpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brand logo uploaded by {UserId} ({Type}, {Bytes} bytes)", User.RequireUserId(), type, data.Length);
        return ToDto(row);
    }

    [HttpDelete("logo"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> DeleteLogo(CancellationToken ct)
    {
        var row = await LoadForWriteAsync(ct);
        row.LogoData = null;
        row.LogoContentType = null;
        row.UpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brand logo removed by {UserId}", User.RequireUserId());
        return ToDto(row);
    }

    [HttpGet("tarpaulin"), AllowAnonymous]
    public async Task<IActionResult> GetTarpaulin(CancellationToken ct)
    {
        var row = await _db.BrandSettings.AsNoTracking().Where(b => b.Id == BrandSettings.SingletonId)
            .Select(b => new { b.TarpaulinData, b.TarpaulinContentType }).FirstOrDefaultAsync(ct);
        return Image(row?.TarpaulinData, row?.TarpaulinContentType);
    }

    [HttpPut("tarpaulin"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(TarpaulinRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = TarpaulinRequestLimit)]
    public async Task<ActionResult<BrandingDto>> PutTarpaulin([FromForm] UploadFileRequest req, CancellationToken ct)
    {
        var (data, type) = await ReadUploadAsync(req.File, TarpaulinTypes, TarpaulinMaxBytes, "Tarpaulin", ct);
        var row = await LoadForWriteAsync(ct);
        row.TarpaulinData = data;
        row.TarpaulinContentType = type;
        row.UpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brand tarpaulin uploaded by {UserId} ({Type}, {Bytes} bytes)", User.RequireUserId(), type, data.Length);
        return ToDto(row);
    }

    [HttpDelete("tarpaulin"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> DeleteTarpaulin(CancellationToken ct)
    {
        var row = await LoadForWriteAsync(ct);
        row.TarpaulinData = null;
        row.TarpaulinContentType = null;
        row.UpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brand tarpaulin removed by {UserId}", User.RequireUserId());
        return ToDto(row);
    }

    private IActionResult Image(byte[]? data, string? contentType) => Uploads.Serve(this, data, contentType);

    private static Task<(byte[] data, string type)> ReadUploadAsync(IFormFile? file, string[] allowed, int maxBytes, string what, CancellationToken ct) =>
        Uploads.ReadAsync(file, allowed, maxBytes, what, ct);
}
