using System.Text.Json;

namespace CafePos.Api.RateLimiting;

/// <summary>
/// The login limit is partitioned by client IP and the email being tried. The rate limiter runs before model binding, so this
/// middleware peeks at the (small) JSON body of POST /api/v1/auth/login, remembers the email in HttpContext.Items and rewinds the
/// body for MVC. Anything unexpected falls back to IP-only partitioning.
/// </summary>
public sealed class LoginEmailCaptureMiddleware(RequestDelegate next)
{
    public const string ItemKey = "ratelimit.login.email";
    private const int MaxBodyBytes = 4096;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase)
            && request.ContentLength is > 0 and <= MaxBodyBytes)
        {
            request.EnableBuffering();
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String)
                    context.Items[ItemKey] = email.GetString()!.Trim().ToLowerInvariant();
            }
            catch (JsonException)
            {
                // malformed body: model binding reports it; the limiter falls back to the IP.
            }
            finally
            {
                request.Body.Position = 0;
            }
        }
        await next(context);
    }
}
