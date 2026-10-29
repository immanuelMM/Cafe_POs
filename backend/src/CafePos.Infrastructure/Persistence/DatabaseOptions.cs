using System.ComponentModel.DataAnnotations;

namespace CafePos.Infrastructure.Persistence;

/// <summary>Database settings. Validated when the host starts.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>ConnectionStrings:Default (never stored in committed settings; user-secrets or environment in real environments).</summary>
    [Required(ErrorMessage = "ConnectionStrings:Default is not configured.")]
    public string ConnectionString { get; set; } = "";

    /// <summary>Apply pending migrations at startup. Development convenience only; production applies a reviewed migration bundle.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Seed dev data into an empty database after migrating (dev and tests only).</summary>
    public bool SeedDevData { get; set; }
}
