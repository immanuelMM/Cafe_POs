namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// First-owner bootstrap settings (configuration section "Bootstrap"; environment variables Bootstrap__OwnerEmail,
/// Bootstrap__OwnerPassword, Bootstrap__OwnerName, Bootstrap__LocationName). Deliberately not validated on start: a bad value
/// is logged by <see cref="OwnerBootstrapper"/> and startup continues, so a typo never crash-loops the container.
/// Remove the password (and email) from the environment after the first sign-in; they are ignored once any user exists.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? OwnerEmail { get; set; }
    public string? OwnerPassword { get; set; }
    /// <summary>Display name of the first owner. Defaults to "Owner".</summary>
    public string? OwnerName { get; set; }
    /// <summary>Name of the first location, created only when no location exists. Defaults to "Main".</summary>
    public string? LocationName { get; set; }
    /// <summary>IANA/system time zone id of the first location (for example "Asia/Manila"). Falls back to the entity default.</summary>
    public string? LocationTimeZone { get; set; }
    /// <summary>VAT percent of the first location, 0 to 100. Falls back to the entity default (12).</summary>
    public decimal? LocationVatRatePercent { get; set; }

    /// <summary>True when either credential is set, i.e. the bootstrapper has something to do or to warn about.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(OwnerEmail) || !string.IsNullOrEmpty(OwnerPassword);
}
