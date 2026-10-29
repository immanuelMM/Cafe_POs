namespace CafePos.Application.Features.Branding;

public record BrandingDto(string BrandName, string? Tagline, string PrimaryColor, string? LogoUrl, string? TarpaulinUrl, string? TarpaulinContentType,
    string? SecondaryColor, string? TertiaryColor, bool Gradient, int GradientAngle);

/// <summary>Design-studio document. Document is opaque JSON owned by the frontend editor.</summary>
public record BrandDesignDto(string Kind, System.Text.Json.JsonElement Document, DateTime UpdatedAt);
