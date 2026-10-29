using CafePos.Infrastructure.Persistence;
using Npgsql;

namespace CafePos.Api.IntegrationTests;

public class DatabaseUrlTests
{
    [Fact]
    public void A_typical_render_url_is_converted_with_secure_defaults()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStringResolver.FromDatabaseUrl("postgresql://cafe_user:s3cret@dpg-abc123-a.oregon-postgres.render.com:5433/cafe_db"));

        Assert.Equal("dpg-abc123-a.oregon-postgres.render.com", b.Host);
        Assert.Equal(5433, b.Port);
        Assert.Equal("cafe_db", b.Database);
        Assert.Equal("cafe_user", b.Username);
        Assert.Equal("s3cret", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
        Assert.Equal(GssEncryptionMode.Disable, b.GssEncryptionMode);
    }

    [Theory]
    [InlineData("postgres://u:p@host/db")]
    [InlineData("postgresql://u:p@host/db")]
    public void The_port_defaults_to_5432_and_both_schemes_are_accepted(string url) =>
        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(ConnectionStringResolver.FromDatabaseUrl(url)).Port);

    [Fact]
    public void Special_characters_in_the_user_and_password_survive_a_round_trip()
    {
        const string user = "us er@x";
        const string password = "p;a=s\"s@w/o?r#d%1";
        var url = $"postgresql://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@host:5432/my%20db";

        var b = new NpgsqlConnectionStringBuilder(ConnectionStringResolver.FromDatabaseUrl(url));

        Assert.Equal(user, b.Username);
        Assert.Equal(password, b.Password);
        Assert.Equal("my db", b.Database);
        // The produced string itself re-parses to the same values (quoting of ; = " in the connection string is correct).
        Assert.Equal(password, new NpgsqlConnectionStringBuilder(b.ConnectionString).Password);
    }

    [Theory]
    [InlineData("verify-full", SslMode.VerifyFull)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("prefer", SslMode.Prefer)]
    [InlineData("require", SslMode.Require)]
    public void An_sslmode_query_parameter_is_honored(string value, SslMode expected) =>
        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(ConnectionStringResolver.FromDatabaseUrl($"postgresql://u:p@host/db?sslmode={value}")).SslMode);

    [Theory]
    [InlineData("mysql://u:p@host/db")]
    [InlineData("not a url")]
    [InlineData("postgresql://u:p@host/db?sslmode=bogus")]
    public void Invalid_urls_fail_without_echoing_the_url(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringResolver.FromDatabaseUrl(url));
        Assert.DoesNotContain("u:p", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_connection_string_always_wins_and_database_url_is_the_fallback()
    {
        Assert.Equal("Host=explicit", ConnectionStringResolver.Resolve("Host=explicit", "postgresql://u:p@other/db"));
        Assert.Contains("other", ConnectionStringResolver.Resolve("", "postgresql://u:p@other/db"), StringComparison.Ordinal);
        Assert.Contains("other", ConnectionStringResolver.Resolve("  ", "postgresql://u:p@other/db"), StringComparison.Ordinal);
        Assert.Null(ConnectionStringResolver.Resolve(null, null));
        Assert.Null(ConnectionStringResolver.Resolve("", ""));
    }
}
