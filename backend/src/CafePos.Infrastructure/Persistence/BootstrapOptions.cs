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
}
