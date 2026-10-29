using System.Net;
using System.Net.Http.Json;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.IntegrationTests;

/// <summary>Refresh-token auditing columns (C5), rate limiting on the auth endpoints (C4) and startup option validation (M8).</summary>
public class AuthHardeningTests
{
    private static ApiFactory NewFactory(params (string key, string value)[] config) =>
        new("cafepos_test_" + Guid.NewGuid().ToString("N"), true, config.ToDictionary(c => c.key, c => (string?)c.value));

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    [Fact]
    public async Task Login_is_rate_limited_per_ip_and_email_with_a_429_problem_and_retry_after()
    {
        using var factory = NewFactory(("RateLimiting:Enabled", "true"), ("RateLimiting:Login:PermitLimit", "3"), ("RateLimiting:Login:WindowSeconds", "60"));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, DevDataSeeder.CashierEmail, "wrong-password")).StatusCode);

        var limited = await Login(client, DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(429, await limited.ProblemStatusAsync());

        // Another account on the same address (a shared kiosk) has its own budget.
        Assert.Equal(HttpStatusCode.OK, (await Login(client, DevDataSeeder.UptownCashierEmail, DevDataSeeder.UptownCashierPassword)).StatusCode);
    }

    [Fact]
    public async Task Refresh_and_switch_location_are_rate_limited()
    {
        using var factory = NewFactory(("RateLimiting:Enabled", "true"), ("RateLimiting:Refresh:PermitLimit", "2"), ("RateLimiting:SwitchLocation:PermitLimit", "2"));
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anonymous.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

        var manager = await factory.ManagerAsync(DevDataSeeder.MainLocationId);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = DevDataSeeder.UptownLocationId })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await manager.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = DevDataSeeder.UptownLocationId })).StatusCode);
    }

    [Fact]
    public async Task The_limits_do_not_affect_other_endpoints()
    {
        using var factory = NewFactory(("RateLimiting:Enabled", "true"), ("RateLimiting:Login:PermitLimit", "1"));
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/branding")).StatusCode);
    }

    [Fact]
    public async Task Rotating_a_refresh_token_links_the_old_one_to_its_replacement_and_a_replay_kills_every_session()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var login = await Login(client, DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword);
        var oldCookie = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal)).Split(';')[0];

        var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var newCookie = refreshed.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal)).Split(';')[0];

        using (var db = factory.NewDb(null))
        {
            var tokens = await db.RefreshTokens.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.NotNull(tokens[0].RevokedAt);
            Assert.Equal(tokens[1].TokenHash, tokens[0].ReplacedByTokenHash);
            Assert.Null(tokens[1].ReplacedByTokenHash);
        }

        var cookieless = factory.CreateClient(new() { HandleCookies = false });
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.SendAsync(replay)).StatusCode);

        var legit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        legit.Headers.Add("Cookie", newCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.SendAsync(legit)).StatusCode); // reuse revoked the whole family
    }

    [Fact]
    public async Task A_missing_or_short_signing_key_fails_startup()
    {
        using var factory = NewFactory(("Jwt:Key", "too-short"));

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Jwt:Key", ex.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>Health endpoints (plan C3): /health stays, /health/live and /health/ready (database) are added.</summary>
public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_public_and_report_healthy(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
