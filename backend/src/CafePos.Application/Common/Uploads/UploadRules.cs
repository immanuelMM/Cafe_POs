namespace CafePos.Application.Common.Uploads;

/// <summary>Validation of user-uploaded images/PDFs that are stored in the database.</summary>
public static class UploadRules
{
    public static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/webp"];

    public static readonly string[] ImageOrPdfTypes = ["image/png", "image/jpeg", "image/webp", "application/pdf"];

    /// <summary>Validates declared content type, size and the file signature; returns the bytes and the (signature-confirmed) type.</summary>
    public static async Task<Result<ValidatedUpload>> ValidateAsync(
        UploadedFile? file, string[] allowed, int maxBytes, string what, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Error.BadRequest("File required", "Send the image in the multipart form field 'file'.");
        var declared = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!allowed.Contains(declared))
            return Error.BadRequest("Unsupported file type", $"{what} must be {string.Join(", ", allowed.Select(a => a.Split('/')[1].ToUpperInvariant()))}.");
        if (file.Length > maxBytes)
            return Error.BadRequest("File too large", $"{what} must be at most {maxBytes / (1024 * 1024)} MB.");

        using var ms = new MemoryStream((int)file.Length);
        await using (var stream = file.OpenRead()) await stream.CopyToAsync(ms, ct);
        var data = ms.ToArray();
        if (DetectType(data) != declared)
            return Error.BadRequest("Unsupported file type", "The file content does not match its declared type.");
        return new ValidatedUpload(data, declared);
    }

    /// <summary>
    /// "Now" for a URL version stamp, truncated to whole milliseconds: PostgreSQL keeps microseconds, so an untruncated
    /// value would make the URL returned by the write differ from the one read back later (and defeat caching).
    /// </summary>
    public static DateTime VersionStamp(TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    /// <summary>The content type implied by the file's magic bytes, or null when it is not a supported format.</summary>
    public static string? DetectType(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return "image/webp";
        if (d.Length >= 5 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F') return "application/pdf";
        return null;
    }
}
