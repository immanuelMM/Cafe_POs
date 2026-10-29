using CafePos.Application.Features.Locations;
using System.ComponentModel.DataAnnotations;
using CafePos.Domain;

namespace CafePos.Api.Dtos;

// ---- Auth ----
public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    /// <summary>Optional; required only when the user is assigned to several locations.</summary>
    public Guid? LocationId { get; init; }
}

/// <summary>Location is null for an Owner's cross-location session. Locations: assignments (all active locations for an Owner).</summary>
public record UserInfoDto(Guid Id, string Email, string DisplayName, string Role, bool IsOwner, LocationRef? Location, IReadOnlyList<LocationRoleDto> Locations);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfoDto User);

public record SwitchLocationRequest
{
    /// <summary>Target location. Owners may send null for the cross-location session; everyone else must send a location.</summary>
    public Guid? LocationId { get; init; }
}

// ---- Branding ----
public record BrandingDto(string BrandName, string? Tagline, string PrimaryColor, string? LogoUrl, string? TarpaulinUrl, string? TarpaulinContentType,
    string? SecondaryColor, string? TertiaryColor, bool Gradient, int GradientAngle);

public record UpdateBrandingRequest
{
    [Required, MaxLength(100)] public string BrandName { get; init; } = "";
    [MaxLength(200)] public string? Tagline { get; init; }
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Primary color must look like #RRGGBB.")] public string PrimaryColor { get; init; } = "";
    /// <summary>Optional; null or empty clears. Empty strings are allowed by the pattern so the client can clear.</summary>
    [RegularExpression("^(#[0-9a-fA-F]{6})?$", ErrorMessage = "Secondary color must look like #RRGGBB.")] public string? SecondaryColor { get; init; }
    [RegularExpression("^(#[0-9a-fA-F]{6})?$", ErrorMessage = "Tertiary color must look like #RRGGBB.")] public string? TertiaryColor { get; init; }
    public bool Gradient { get; init; }
    [Range(0, 360)] public int GradientAngle { get; init; } = BrandSettings.DefaultGradientAngle;
}

// ---- Time clock ----
public record TimeClockStaffDto(Guid UserId, string DisplayName, string Role, bool HasPin, bool ClockedIn, DateTime? OpenSince);
public record TimeEntryDto(Guid Id, Guid UserId, string DisplayName, Guid LocationId, DateTime ClockInAt, DateTime? ClockOutAt, decimal? Hours);
public record PunchResultDto(string Action, TimeEntryDto Entry);

public record PunchRequest
{
    [Required] public Guid? UserId { get; init; }
    [Required] public string Pin { get; init; } = "";
}

/// <summary>Design-studio document. Document is opaque JSON owned by the frontend editor.</summary>
public record BrandDesignDto(string Kind, System.Text.Json.JsonElement Document, DateTime UpdatedAt);
public record SaveBrandDesignRequest
{
    [Required] public System.Text.Json.JsonElement Document { get; init; }
}

// ---- Locations ----

// ---- Users ----

// ---- Variation location overrides ----

// ---- Catalog ----

// ---- Inventory ----

// ---- Orders ----

