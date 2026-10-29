using System.Net;
using CafePos.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.HttpOverrides;

namespace CafePos.Api.IntegrationTests;

/// <summary>ForwardedHeaders:ForwardLimit for requests that cross several trusted proxies.</summary>
public class ForwardLimitTests
{
    private static readonly ForwardedHeadersSettingsValidator Validator = new();

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    [InlineData(-2, false)]
    public void ForwardLimit_must_be_at_least_one(int limit, bool valid) =>
        Assert.Equal(valid, Validator.Validate(null, new ForwardedHeadersSettings { ForwardLimit = limit }).Succeeded);

    [Fact]
    public void ForwardLimit_defaults_to_one() => Assert.Equal(1, new ForwardedHeadersSettings().ForwardLimit);

    private static async Task<string?> ClientIpAsync(Dictionary<string, string?> config, string remote, string xff)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddProxyTrust().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, options);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        http.Request.Headers["X-Forwarded-For"] = xff;
        await middleware.Invoke(http);
        return ClientIp.Of(http);
    }

    private static Dictionary<string, string?> Config(int? limit)
    {
        var config = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1",
            ["ForwardedHeaders:KnownProxies:1"] = "10.0.0.2"
        };
        if (limit is not null) config["ForwardedHeaders:ForwardLimit"] = limit.ToString();
        return config;
    }

    [Fact]
    public async Task The_default_limit_resolves_to_the_last_hop() =>
        Assert.Equal("10.0.0.2", await ClientIpAsync(Config(null), "10.0.0.1", "203.0.113.9, 10.0.0.2"));

    [Fact]
    public async Task A_higher_limit_unwinds_every_trusted_hop_to_the_real_client() =>
        Assert.Equal("203.0.113.9", await ClientIpAsync(Config(2), "10.0.0.1", "203.0.113.9, 10.0.0.2"));

    [Fact]
    public async Task Spoofed_entries_beyond_the_trusted_proxies_are_not_honored_even_with_a_generous_limit() =>
        // The client sent 6.6.6.6 itself; 203.0.113.9 is not a trusted proxy, so the walk stops there.
        Assert.Equal("203.0.113.9", await ClientIpAsync(Config(5), "10.0.0.1", "6.6.6.6, 203.0.113.9, 10.0.0.2"));

    [Fact]
    public async Task An_untrusted_sender_is_ignored_whatever_the_limit() =>
        Assert.Equal("198.51.100.7", await ClientIpAsync(Config(5), "198.51.100.7", "203.0.113.9, 10.0.0.2"));
}
