using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Infrastructure;

/// <summary>Multipart body with the uploaded file in the form field "file".</summary>
public class UploadFileRequest
{
    [FromForm(Name = "file")] public IFormFile? File { get; set; }
}

/// <summary>Validation and serving of user-uploaded images/PDFs stored in the database.</summary>
public static class Uploads
{
    public static readonly string[] ImageTypes = { "image/png", "image/jpeg", "image/webp" };
    public static readonly string[] ImageOrPdfTypes = { "image/png", "image/jpeg", "image/webp", "application/pdf" };

    /// <summary>Adapts a multipart file to the framework-independent upload the Application layer validates.</summary>
    public static CafePos.Application.Common.Uploads.UploadedFile? ToUploadedFile(IFormFile? file) =>
        file is null ? null : new CafePos.Application.Common.Uploads.UploadedFile(file.ContentType, file.Length, file.OpenReadStream);

    /// <summary>Validates declared content type, size and the file signature; returns the bytes and the (signature-confirmed) type.</summary>
    public static async Task<(byte[] data, string type)> ReadAsync(IFormFile? file, string[] allowed, int maxBytes, string what, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw ApiException.BadRequest("File required", "Send the image in the multipart form field 'file'.");
        var declared = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!allowed.Contains(declared))
            throw ApiException.BadRequest("Unsupported file type", $"{what} must be {string.Join(", ", allowed.Select(a => a.Split('/')[1].ToUpperInvariant()))}.");
        if (file.Length > maxBytes)
            throw ApiException.BadRequest("File too large", $"{what} must be at most {maxBytes / (1024 * 1024)} MB.");

        using var ms = new MemoryStream((int)file.Length);
        await using (var s = file.OpenReadStream()) await s.CopyToAsync(ms, ct);
        var data = ms.ToArray();
        if (DetectType(data) != declared)
            throw ApiException.BadRequest("Unsupported file type", "The file content does not match its declared type.");
        return (data, declared);
    }

    /// <summary>
    /// "Now" for a URL version stamp, truncated to whole milliseconds: PostgreSQL keeps microseconds, so an untruncated
    /// value would make the URL returned by the write differ from the one read back later (and defeat caching).
    /// </summary>
    public static DateTime VersionStamp()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    /// <summary>Serves stored bytes with long-lived caching (callers version the URL) and nosniff; 404 when nothing is stored.</summary>
    public static IActionResult Serve(ControllerBase c, byte[]? data, string? contentType)
    {
        if (data is null || contentType is null) return c.NotFound();
        c.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        c.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // CORS headers depend on the Origin: without this a cached plain <img> copy (no ACAO) is reused for
        // crossOrigin="anonymous" canvas loads in the design studio and the browser blocks them.
        c.Response.Headers.Vary = "Origin";
        return c.File(data, contentType);
    }

    private static string? DetectType(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return "image/webp";
        if (d.Length >= 5 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F') return "application/pdf";
        return null;
    }
}
