using System.Text.Json;
using CafePos.Api.Http;

namespace CafePos.Api.RateLimiting;

/// <summary>
/// The rate limiter runs before model binding, so this middleware peeks at the (small) JSON body of the two brute-forceable
/// endpoints and remembers the value an attacker would be guessing against, in HttpContext.Items, then rewinds the body for MVC:
/// the email of POST /api/v1/auth/login and the target userId of POST /api/v1/timeclock/punch.
/// <para>
/// MVC binds JSON property names case-insensitively, so the key must be read the same way, and a body that carries the key twice
/// (for example "email" and "Email") is ambiguous: the limiter would see one value and the binder another. Such bodies are rejected
/// with a 400 here, before any limiter runs. A body that cannot be read otherwise falls back to IP-only partitioning.
/// </para>
/// </summary>
public sealed class RateLimitKeyCaptureMiddleware(RequestDelegate next)
{
    public const string LoginEmailItem = "ratelimit.login.email";
    public const string PunchUserItem = "ratelimit.punch.user";
    private const int MaxBodyBytes = 4096;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsPost(request.Method) && request.ContentLength is > 0 and <= MaxBodyBytes)
        {
            if (request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
            {
                if (!await CaptureAsync(context, "email", LoginEmailItem, NormalizeEmail)) return;
            }
            else if (request.Path.Equals("/api/v1/timeclock/punch", StringComparison.OrdinalIgnoreCase))
            {
                if (!await CaptureAsync(context, "userId", PunchUserItem, NormalizeGuid)) return;
            }
        }
        await next(context);
    }

    /// <summary>The same normalisation ASP.NET Core Identity applies when it looks an email up (Normalize + ToUpperInvariant); surrounding whitespace is dropped too so padded variants share a bucket.</summary>
    public static string NormalizeEmail(string email) => email.Trim().Normalize().ToUpperInvariant();

    private static string? NormalizeGuid(string value) => Guid.TryParse(value, out var id) ? id.ToString("D") : null;

    /// <summary>Returns false when the request was answered (400) and the pipeline must stop.</summary>
    private static async Task<bool> CaptureAsync(HttpContext context, string property, string itemKey, Func<string, string?> normalize)
    {
        var request = context.Request;
        request.EnableBuffering();
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return true;

            JsonElement? found = null;
            foreach (var member in doc.RootElement.EnumerateObject())
            {
                if (!member.NameEquals(property) && !string.Equals(member.Name, property, StringComparison.OrdinalIgnoreCase)) continue;
                if (found is not null)
                {
                    await RejectAsync(context, property);
                    return false;
                }
                found = member.Value;
            }
            if (found is { ValueKind: JsonValueKind.String } value && normalize(value.GetString()!) is { } key)
                context.Items[itemKey] = key;
        }
        catch (JsonException)
        {
            // malformed body: model binding reports it; the limiter falls back to the IP.
        }
        finally
        {
            request.Body.Position = 0;
        }
        return true;
    }

    private static Task RejectAsync(HttpContext context, string property) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [property] = [$"The '{property}' property must appear only once."] })
            .ExecuteAsync(context);
}
