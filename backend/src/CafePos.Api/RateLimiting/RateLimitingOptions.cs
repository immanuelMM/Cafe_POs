using System.ComponentModel.DataAnnotations;

namespace CafePos.Api.RateLimiting;

/// <summary>
/// Rate limits for the authentication endpoints (configuration section "RateLimiting"). Fixed windows; exceeding a limit returns 429
/// with a Retry-After header. The defaults suit a shared kiosk: login is limited per client IP and email address (so several staff
/// signing in from one tablet do not share a budget), refresh per IP, switch-location per signed-in user.
/// </summary>
public sealed class RateLimitingOptions : IValidatableObject
{
    public const string SectionName = "RateLimiting";

    /// <summary>Set to false to disable all limits (never in production).</summary>
    public bool Enabled { get; set; } = true;

    [Required] public RateLimitRule Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Coarser second login limit per client IP across all emails, so rotating the email cannot dodge the per-email budget.</summary>
    [Required] public RateLimitRule LoginPerIp { get; set; } = new() { PermitLimit = 50, WindowSeconds = 60 };

    [Required] public RateLimitRule Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    [Required] public RateLimitRule SwitchLocation { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Time clock punches, per client IP and target staff member (the PIN being guessed).</summary>
    [Required] public RateLimitRule TimeclockPunch { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>DataAnnotations does not recurse into nested objects, so each rule is validated here (a zero limit would 500 at runtime).</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, rule) in new[]
                 {
                     (nameof(Login), Login), (nameof(LoginPerIp), LoginPerIp), (nameof(Refresh), Refresh),
                     (nameof(SwitchLocation), SwitchLocation), (nameof(TimeclockPunch), TimeclockPunch)
                 })
        {
            if (rule is null) continue; // reported by [Required]
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(rule, new ValidationContext(rule), results, validateAllProperties: true);
            foreach (var r in results)
                yield return new ValidationResult($"{SectionName}:{name}: {r.ErrorMessage}", r.MemberNames.Select(m => $"{name}.{m}"));
        }
    }
}

public sealed class RateLimitRule
{
    [Range(1, 100_000)] public int PermitLimit { get; set; }

    [Range(1, 3600)] public int WindowSeconds { get; set; }
}
