using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CafePos.Api.RateLimiting;

public static class RateLimitingSetup
{
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        // Configured through IOptions so test hosts and environment overrides applied after startup are honored.
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>, ILoggerFactory>((limiter, config, loggers) =>
        {
            var settings = config.Value;
            var logger = loggers.CreateLogger("CafePos.Api.RateLimiting");
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(RateLimitPolicies.Login, http =>
                Partition(settings.Enabled, settings.Login, $"login:{Ip(http)}|{http.Items[LoginEmailCaptureMiddleware.ItemKey]}"));
            limiter.AddPolicy(RateLimitPolicies.Refresh, http =>
                Partition(settings.Enabled, settings.Refresh, $"refresh:{Ip(http)}"));
            limiter.AddPolicy(RateLimitPolicies.SwitchLocation, http =>
                Partition(settings.Enabled, settings.SwitchLocation, $"switch:{http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ip(http)}"));

            limiter.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? (int)Math.Ceiling(delay.TotalSeconds) : 60;
                http.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                logger.LogWarning("Rate limit exceeded on {Path} from {Ip}", http.Request.Path, Ip(http));
                var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new()
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = $"Too many attempts. Try again in {retryAfter} seconds."
                    }
                });
            };
        });
        return services;
    }

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> Partition(bool enabled, RateLimitRule rule, string key) =>
        enabled
            ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            })
            : RateLimitPartition.GetNoLimiter(key);
}
