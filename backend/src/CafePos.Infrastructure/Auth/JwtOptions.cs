using System.ComponentModel.DataAnnotations;

namespace CafePos.Infrastructure.Auth;

/// <summary>JWT settings (configuration section "Jwt"). Validated when the host starts; a bad configuration fails startup.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "CafePos";

    [Required]
    public string Audience { get; set; } = "CafePos.Client";

    /// <summary>HMAC signing key (min 32 chars). Never committed for real environments: supply it via Jwt__Key or user-secrets.</summary>
    [Required(ErrorMessage = "Jwt:Key must be set (min 32 characters). Supply it via the Jwt__Key environment variable or user-secrets; it is never committed.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be set (min 32 characters). Supply it via the Jwt__Key environment variable or user-secrets; it is never committed.")]
    public string Key { get; set; } = "";

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 30;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
