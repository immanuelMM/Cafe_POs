using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// Chooses the database connection string. Precedence: an explicit non-empty ConnectionStrings:Default always wins; otherwise a
/// DATABASE_URL (the form Render and Heroku expose, postgresql://user:pass@host:port/db) is converted to an Npgsql connection string.
/// </summary>
public static class ConnectionStringResolver
{
    public const string DatabaseUrlKey = "DATABASE_URL";

    public static string? Resolve(IConfiguration configuration) =>
        Resolve(configuration.GetConnectionString("Default"), configuration[DatabaseUrlKey]);

    public static string? Resolve(string? explicitConnectionString, string? databaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(explicitConnectionString)) return explicitConnectionString;
        return string.IsNullOrWhiteSpace(databaseUrl) ? null : FromDatabaseUrl(databaseUrl);
    }

    /// <summary>
    /// Defaults: SslMode=Require, (Require does not verify the self-signed internal certificate; sslmode=verify-ca/verify-full in the URL turns verification on), GSS encryption off, port 5432. User and password are URL-decoded. The URL is never echoed in errors
    /// because it contains the password.
    /// </summary>
    public static string FromDatabaseUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres")
            || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("DATABASE_URL is not a valid postgresql://user:password@host:port/database URL.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
            SslMode = SslMode.Require,
            GssEncryptionMode = GssEncryptionMode.Disable
        };
        if (uri.UserInfo.Length > 0)
        {
            var colon = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);
            builder.Username = Uri.UnescapeDataString(colon < 0 ? uri.UserInfo : uri.UserInfo[..colon]);
            if (colon >= 0) builder.Password = Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || !string.Equals(pair[..eq], "sslmode", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]).Replace("-", "", StringComparison.Ordinal);
            if (!Enum.TryParse<SslMode>(value, ignoreCase: true, out var mode))
                throw new InvalidOperationException("DATABASE_URL has an unsupported sslmode value.");
            builder.SslMode = mode;
        }
        // SslMode.Require encrypts without validating the server certificate (Npgsql 10: the separate TrustServerCertificate
        // option is obsolete and does nothing), which is what a platform's self-signed internal TLS needs. verify-ca/verify-full keep validation.
        return builder.ConnectionString;
    }
}
