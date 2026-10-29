using CafePos.Domain;

namespace CafePos.Application.Features.Branding;

public record UpdateBrandingRequest
{
    public string BrandName { get; init; } = "";
    public string? Tagline { get; init; }
    public string PrimaryColor { get; init; } = "";
    /// <summary>Optional; null or empty clears. Empty strings are allowed by the pattern so the client can clear.</summary>
    public string? SecondaryColor { get; init; }
    public string? TertiaryColor { get; init; }
    public bool Gradient { get; init; }
    public int GradientAngle { get; init; } = BrandSettings.DefaultGradientAngle;
}

public record SaveBrandDesignRequest
{
    public System.Text.Json.JsonElement Document { get; init; }
}
