namespace CafePos.Domain;

/// <summary>An editable design-studio document (JSON) for the logo or the banner, so it can be reopened and changed later.</summary>
public class BrandDesign
{
    public static readonly string[] Kinds = { "logo", "banner" };
    public string Kind { get; set; } = "";
    public string Document { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
