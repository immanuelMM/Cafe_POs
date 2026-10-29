using System.Net;
using System.Net.Http.Json;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Api.IntegrationTests;

/// <summary>First-owner bootstrap from the "Bootstrap" configuration section (production never seeds).</summary>
public class OwnerBootstrapTests
{
    private const string Email = "first.owner@example.com";
    private const string Password = "Str0ng#Bootstrap1";

    private static ApiFactory NewFactory(bool seed, params (string key, string value)[] config) =>
        new("cafepos_test_" + Guid.NewGuid().ToString("N"), seed, config.ToDictionary(c => c.key, c => (string?)c.value));

    private static (string, string)[] Full(params (string, string)[] extra) =>
        [("Bootstrap:OwnerEmail", Email), ("Bootstrap:OwnerPassword", Password), .. extra];

    [Fact]
    public async Task Creates_the_owner_and_a_first_location_on_an_empty_database_and_the_owner_can_log_in()
    {
        using var factory = NewFactory(false, Full(("Bootstrap:OwnerName", "Ola Owner"), ("Bootstrap:LocationName", "Downtown")));
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = factory.NewDb(null);
        var user = await db.Users.SingleAsync();
        Assert.True(user.IsOwner);
        Assert.Equal("Ola Owner", user.DisplayName);
        Assert.Equal("Downtown", (await db.Locations.IgnoreQueryFilters().SingleAsync()).Name);
        Assert.Empty(await db.StaffAssignments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Uses_default_names_when_optional_settings_are_absent()
    {
        using var factory = NewFactory(false, Full());
        _ = factory.Services;

        using var db = factory.NewDb(null);
        Assert.Equal("Owner", (await db.Users.SingleAsync()).DisplayName);
        Assert.Equal("Main", (await db.Locations.IgnoreQueryFilters().SingleAsync()).Name);
    }

    [Fact]
    public async Task Running_again_is_a_no_op_and_never_overwrites_the_owner()
    {
        using var factory = NewFactory(false, Full());
        _ = factory.Services;

        // A second start (or second instance) with a different password and location name changes nothing.
        using var second = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Bootstrap:OwnerPassword"] = "An0ther#Password9", ["Bootstrap:LocationName"] = "Other" })));
        _ = second.Services;

        using var db = factory.NewDb(null);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("Main", (await db.Locations.IgnoreQueryFilters().SingleAsync()).Name);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_runs_create_exactly_one_owner_and_one_location()
    {
        using var factory = NewFactory(false, Full());
        _ = factory.Services;
        using (var clean = factory.NewDb(null))
        {
            await clean.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"AspNetUsers\", \"Locations\" CASCADE");
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            await OwnerBootstrapper.RunAsync(scope.ServiceProvider, NullLogger.Instance);
        }));

        using var db = factory.NewDb(null);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Locations.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Does_nothing_when_a_user_already_exists()
    {
        using var factory = NewFactory(true, Full(("Bootstrap:LocationName", "Extra")));
        _ = factory.Services;

        using var db = factory.NewDb(null);
        Assert.False(await db.Users.AnyAsync(u => u.Email == Email));
        Assert.False(await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.Name == "Extra"));
    }

    [Fact]
    public async Task Does_nothing_when_unset_or_only_half_set()
    {
        (string, string)[][] cases = [[], [("Bootstrap:OwnerEmail", Email)], [("Bootstrap:OwnerPassword", Password)]];
        foreach (var config in cases)
        {
            using var factory = NewFactory(false, config);
            _ = factory.Services;
            using var db = factory.NewDb(null);
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.Locations.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async Task A_weak_password_creates_nothing_but_the_app_still_starts()
    {
        using var factory = NewFactory(false, ("Bootstrap:OwnerEmail", Email), ("Bootstrap:OwnerPassword", "short"));

        var health = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var db = factory.NewDb(null);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Locations.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("alice@example.com", "a***@e***.com")]
    [InlineData("a@b", "a***@b***")]
    [InlineData("nonsense", "n***")]
    public void Masked_emails_do_not_disclose_the_address(string email, string expected) =>
        Assert.Equal(expected, OwnerBootstrapper.Mask(email));
}
