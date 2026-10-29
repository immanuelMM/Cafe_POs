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
    
    /// <summary>Adapts a multipart file to the framework-independent upload the Application layer validates.</summary>
    public static CafePos.Application.Common.Uploads.UploadedFile? ToUploadedFile(IFormFile? file) =>
        file is null ? null : new CafePos.Application.Common.Uploads.UploadedFile(file.ContentType, file.Length, file.OpenReadStream);

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
}
