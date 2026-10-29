using System.Net;
using System.Net.Http.Json;
using System.Text;
using CafePos.Api.Http;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Api.IntegrationTests;

/// <summary>Security review fixes: rate-limit partitioning, forwarded headers, refresh/lockout/PIN races, option validation, cookie flags.</summary>
public class ReviewFixTests
{
    private static ApiFactory NewFactory(params (string key, string value)[] config) =>
        new("cafepos_test_" + Guid.NewGuid().ToString("N"), true, config.ToDictionary(c => c.key, c => (string?)c.value));

    private static Task<HttpResponseMessage> RawLogin(HttpClient client, string json) =>
        client.PostAsync("/api/v1/auth/login", new StringContent(json, Encoding.UTF8, "application/json"));

    private static string LoginBody(string email, string password, string emailKey = "email") =>
        $"{{\"{emailKey}\":\"{email}\",\"password\":\"{password}\"}}";

    private static (string key, string value)[] Limits(int login = 3, int perIp = 1000, int punch = 1000) =>
    [
        ("RateLimiting:Enabled", "true"), ("RateLimiting:Login:PermitLimit", login.ToString()),
        ("RateLimiting:LoginPerIp:PermitLimit", perIp.ToString()), ("RateLimiting:TimeclockPunch:PermitLimit", punch.ToString())
    ];

    // ---- 1. login rate-limit partitioning ----

    [Fact]
    public async Task A_rotating_email_with_a_differently_cased_duplicate_key_is_rejected_not_given_a_fresh_bucket()
    {
        using var factory = NewFactory(Limits());
        var client = factory.CreateClient();

        for (var i = 0; i < 6; i++)
        {
            var body = $"{{\"email\":\"rand-{i}@x.test\",\"Email\":\"{DevDataSeeder.CashierEmail}\",\"password\":\"wrong-password\"}}";
            var res = await RawLogin(client, body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(400, await res.ProblemStatusAsync());
        }
        // A rejected body never reached the credential check: the account is not locked and still logs in.
        Assert.Equal(HttpStatusCode.OK, (await RawLogin(client, LoginBody(DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword))).StatusCode);
    }

    [Fact]
    public async Task Differently_cased_keys_and_values_share_one_login_bucket()
    {
        using var factory = NewFactory(Limits());
        var client = factory.CreateClient();
        string[] bodies =
        [
            LoginBody(DevDataSeeder.CashierEmail, "x", "EMAIL"),
            LoginBody(DevDataSeeder.CashierEmail.ToUpperInvariant(), "x", "Email"),
            LoginBody("  " + DevDataSeeder.CashierEmail + " ", "x")
        ];
        foreach (var body in bodies)
            Assert.Equal(HttpStatusCode.Unauthorized, (await RawLogin(client, body)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RawLogin(client, LoginBody(DevDataSeeder.CashierEmail, "x"))).StatusCode);
    }

    [Fact]
    public async Task A_coarse_per_ip_login_limit_applies_across_emails()
    {
        using var factory = NewFactory(Limits(login: 100, perIp: 3));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await RawLogin(client, LoginBody($"rand-{i}@x.test", "x"))).StatusCode);

        var limited = await RawLogin(client, LoginBody("rand-99@x.test", "x"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Duplicate_json_keys_are_a_400_for_every_body()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var res = await RawLogin(client, "{\"email\":\"a@b.test\",\"password\":\"x\",\"password\":\"y\"}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---- 2. forwarded headers, cookie, HSTS ----

    [Fact]
    public async Task X_forwarded_for_is_not_trusted_unless_a_proxy_is_configured()
    {
        using var factory = NewFactory(Limits(login: 1000, perIp: 2));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = new StringContent(LoginBody($"rand-{i}@x.test", "x"), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            var res = await client.SendAsync(req);
            Assert.Equal(i < 2 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, res.StatusCode);
        }
    }

    [Fact]
    public void An_invalid_trusted_proxy_fails_startup()
    {
        using var factory = NewFactory(("ForwardedHeaders:KnownProxies:0", "not-an-ip"));

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("ForwardedHeaders:KnownProxies", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_refresh_cookie_is_Secure_outside_Development_even_over_http()
    {
        using var factory = NewFactory();
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient(new() { AllowAutoRedirect = false });

        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });

        res.EnsureSuccessStatusCode();
        var cookie = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie.Split(';').Select(a => a.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_client_ip_drops_an_ipv6_zone_id_and_fits_the_column()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("fe80::1ff:fe23:4567:890a%some-long-interface-name");

        var ip = ClientIp.Of(http);

        Assert.Equal("fe80::1ff:fe23:4567:890a", ip);
        Assert.True(ip!.Length <= ClientIp.MaxLength);
        Assert.Null(ClientIp.Of(new DefaultHttpContext()));
    }

    // ---- 3. refresh rotation race ----

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_yield_exactly_one_success_and_then_revoke_the_family()
    {
        using var factory = NewFactory();
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal)).Split(';')[0];
        var cookieless = factory.CreateClient(new() { HandleCookies = false });

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            req.Headers.Add("Cookie", cookie);
            return (await cookieless.SendAsync(req)).StatusCode;
        }));

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
        Assert.All(results.Where(s => s != HttpStatusCode.OK), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        using var db = factory.NewDb(null);
        Assert.DoesNotContain(await db.RefreshTokens.AsNoTracking().ToListAsync(), t => t.RevokedAt is null); // reuse revoked the family
    }

    // ---- 1b. lockout race ----

    [Fact]
    public async Task Concurrent_bad_passwords_never_500_and_all_count_towards_the_lockout()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => RawLogin(client, LoginBody(DevDataSeeder.CashierEmail, "wrong"))));

        var statuses = string.Join(",", results.Select(r => (int)r.StatusCode));
        Assert.True(results.All(r => (int)r.StatusCode is 401 or 423), "statuses: " + statuses);
        var after = await RawLogin(client, LoginBody(DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword));
        Assert.True(after.StatusCode == (HttpStatusCode)423, $"after: {(int)after.StatusCode}; statuses: {statuses}");
    }

    // ---- 4. time clock PIN ----

    [Fact]
    public async Task Concurrent_wrong_pins_all_count_and_lock_the_pin()
    {
        using var factory = NewFactory();
        var owner = await factory.AdminAsync();
        var kiosk = await factory.CashierAsync();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var email = $"clock-{Guid.NewGuid():N}@cafe.local";
            var u = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Racer" };
            Assert.True((await users.CreateAsync(u, "Clock#Test12345")).Succeeded);
            db.StaffAssignments.Add(new StaffAssignment { UserId = u.Id, LocationId = DevDataSeeder.MainLocationId, Role = Roles.Cashier });
            await db.SaveChangesAsync();
            id = u.Id;
        }
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/v1/users/{id}/pin", new { pin = "1111" })).StatusCode);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => kiosk.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = id, pin = "9999" })));

        Assert.DoesNotContain(results, r => (int)r.StatusCode >= 500);
        using var check = factory.NewDb(null);
        var row = await check.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.NotNull(row.ClockPinLockedUntil); // five parallel misses must reach the limit; a lost update would leave it unlocked
        Assert.Equal(0, row.ClockPinFailedCount);
    }

    [Fact]
    public async Task Punching_is_rate_limited_per_target_staff_member()
    {
        using var factory = NewFactory(Limits(punch: 3));
        var kiosk = await factory.CashierAsync();
        var target = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await kiosk.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = target, pin = "1234" })).StatusCode);
        var limited = await kiosk.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = target, pin = "1234" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        // Another staff member has their own budget.
        Assert.Equal(HttpStatusCode.NotFound, (await kiosk.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = Guid.NewGuid(), pin = "1234" })).StatusCode);
    }

    // ---- 5. nested option validation ----

    [Theory]
    [InlineData("RateLimiting:Login:PermitLimit", "0")]
    [InlineData("RateLimiting:LoginPerIp:WindowSeconds", "0")]
    [InlineData("RateLimiting:TimeclockPunch:PermitLimit", "0")]
    [InlineData("RateLimiting:Refresh:WindowSeconds", "999999")]
    public void An_invalid_nested_rate_limit_rule_fails_startup(string key, string value)
    {
        using var factory = NewFactory((key, value));

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("RateLimiting", ex.ToString(), StringComparison.Ordinal);
    }
}
