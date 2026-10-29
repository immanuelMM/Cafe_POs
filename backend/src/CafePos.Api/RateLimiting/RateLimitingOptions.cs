using System.ComponentModel.DataAnnotations;

namespace CafePos.Api.RateLimiting;

/// <summary>
/// Rate limits for the authentication endpoints (configuration section "RateLimiting"). Fixed windows; exceeding a limit returns 429
/// with a Retry-After header. The defaults suit a shared kiosk: login is limited per client IP and email address (so several staff
/// signing in from one tablet do not share a budget), refresh per IP, switch-location per signed-in user.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Set to false to disable all limits (never in production).</summary>
    public bool Enabled { get; set; } = true;

    [Required] public RateLimitRule Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    [Required] public RateLimitRule Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    [Required] public RateLimitRule SwitchLocation { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
}

public sealed class RateLimitRule
{
    [Range(1, 100_000)] public int PermitLimit { get; set; }

    [Range(1, 3600)] public int WindowSeconds { get; set; }
}
