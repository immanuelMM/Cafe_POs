namespace CafePos.Domain;

/// <summary>Business-wide branding shown in the app header and login screen. A single row (Id = SingletonId).</summary>
public class BrandSettings
{
    public const int SingletonId = 1;
    public const string DefaultBrandName = "Brew & Bean";
    public int Id { get; set; } = SingletonId;
    public const string DefaultPrimaryColor = "#3b2417";
    public string BrandName { get; set; } = DefaultBrandName;
    public string? Tagline { get; set; }
    public string PrimaryColor { get; set; } = DefaultPrimaryColor;
    /// <summary>Optional 2nd and 3rd brand colors (#rrggbb). Tertiary requires secondary.</summary>
    public string? SecondaryColor { get; set; }
    public string? TertiaryColor { get; set; }
    /// <summary>When true, brand surfaces use a linear gradient of the colors at GradientAngle degrees; otherwise solid colors.</summary>
    public bool Gradient { get; set; }
    public const int DefaultGradientAngle = 135;
    public int GradientAngle { get; set; } = DefaultGradientAngle;
    public byte[]? LogoData { get; set; }
    public string? LogoContentType { get; set; }
    public byte[]? TarpaulinData { get; set; }
    public string? TarpaulinContentType { get; set; }
    /// <summary>Bumped on every image upload or removal; its ticks are the `v` cache-busting value on the image URLs.</summary>
    public DateTime UpdatedAt { get; set; }
}
