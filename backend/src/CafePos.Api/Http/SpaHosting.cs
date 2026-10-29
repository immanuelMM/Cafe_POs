using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;

namespace CafePos.Api.Http;

/// <summary>
/// Optional single-service hosting of the built SPA (configuration "Spa:RootPath", env Spa__RootPath). Unset (the default) does
/// nothing, so dev, compose and tests are unchanged. When set to a directory containing index.html the API serves its static
/// files and falls back to index.html for client-side routes. The API's own paths (/api, /health, /openapi, /scalar) are never
/// answered by the SPA: unknown ones keep returning the normal 404 problem response. The fallback is excluded from OpenAPI.
/// </summary>
public static class SpaHosting
{
    public const string RootPathKey = "Spa:RootPath";
    private static readonly string[] ReservedPrefixes = ["/api", "/health", "/openapi", "/scalar"];

    public static bool IsReserved(PathString path) =>
        ReservedPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the file provider to serve from, or null when the SPA is not configured or its directory is unusable.</summary>
    public static IFileProvider? Resolve(IConfiguration configuration, IWebHostEnvironment env, ILogger logger)
    {
        var root = configuration[RootPathKey];
        if (string.IsNullOrWhiteSpace(root)) return null;
        var full = Path.GetFullPath(root.Trim(), env.ContentRootPath);
        if (!File.Exists(Path.Combine(full, "index.html")))
        {
            logger.LogError("Spa:RootPath '{Path}' does not exist or has no index.html; running as API only.", full);
            return null;
        }
        logger.LogInformation("Serving the SPA from {Path}.", full);
        return new PhysicalFileProvider(full);
    }

    /// <summary>Static files for everything outside the API's own paths. Add after HTTPS redirection, before authentication.</summary>
    public static void UseSpaStaticFiles(this WebApplication app, IFileProvider files) =>
        app.UseWhen(ctx => !IsReserved(ctx.Request.Path), branch => branch.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                // Vite emits content-hashed names under /assets; everything else (index.html, favicon) must revalidate.
                headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            }
        }));

    /// <summary>index.html for unmatched GET/HEAD navigations; add after MapControllers. Other unknown paths stay 404.</summary>
    public static void MapSpaFallback(this WebApplication app, IFileProvider files) =>
        app.MapFallback("{*path}", async ctx =>
        {
            var index = files.GetFileInfo("index.html");
            var isNavigation = (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                               && !IsReserved(ctx.Request.Path)
                               && string.IsNullOrEmpty(Path.GetExtension(ctx.Request.Path.Value)); // a missing /x.js is a 404, not HTML
            if (!isNavigation || !index.Exists)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound; // UseStatusCodePages turns this into the problem response
                return;
            }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            await ctx.Response.SendFileAsync(index, ctx.RequestAborted);
        }).AllowAnonymous().ExcludeFromDescription();
}
