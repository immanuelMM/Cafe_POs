using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CafePos.Api.Tests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL database (created from migrations, seeded with dev data).
/// By default a PostgreSQL 16 Testcontainers container is started once per test run (needs a running Docker engine,
/// nothing else). Set TEST_PG_CONNECTION to use an existing server instead (e.g. a CI service container); the
/// Database part is replaced with a unique name per factory.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "cafepos_test_" + Guid.NewGuid().ToString("N");
    private readonly NpgsqlConnectionStringBuilder _csb;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiFactory()
    {
        var baseCs = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
                     ?? PostgresContainer.ConnectionString;
        _csb = new NpgsqlConnectionStringBuilder(baseCs) { Database = _dbName };
    }

    public string ConnectionString => _csb.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Jwt:Key"] = "test-signing-key-test-signing-key-test-signing-key",
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedDevData"] = "true"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            NpgsqlConnection.ClearAllPools();
            var admin = new NpgsqlConnectionStringBuilder(_csb.ConnectionString) { Database = "postgres", Pooling = false };
            using var conn = new NpgsqlConnection(admin.ConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", conn);
            cmd.ExecuteNonQuery();
        }
        catch { /* best effort cleanup */ }
    }

    // ---- helpers ----
    public async Task<HttpClient> LoginAsync(string email, string password, Guid? locationId = null)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, locationId });
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    /// <summary>The seeded Owner, switched into the Main location (location-scoped work needs an active location).</summary>
    public Task<HttpClient> AdminAsync() => LoginAsync(DevDataSeeder.AdminEmail, DevDataSeeder.AdminPassword, DevDataSeeder.MainLocationId);
    /// <summary>The seeded Owner in the cross-location session (no active location).</summary>
    public Task<HttpClient> OwnerAsync() => LoginAsync(DevDataSeeder.AdminEmail, DevDataSeeder.AdminPassword);
    public Task<HttpClient> ManagerAsync(Guid locationId) => LoginAsync(DevDataSeeder.ManagerEmail, DevDataSeeder.ManagerPassword, locationId);
    public Task<HttpClient> UptownCashierAsync() => LoginAsync(DevDataSeeder.UptownCashierEmail, DevDataSeeder.UptownCashierPassword);
    public Task<HttpClient> CashierAsync() => LoginAsync(DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword);

    /// <summary>A DbContext scoped to a given location (null = system context with no location).</summary>
    public CafeDbContext NewDb(Guid? locationId)
    {
        _ = Services; // boot the host first so the database exists (migrate-on-startup); otherwise this depends on test order
        var options = new DbContextOptionsBuilder<CafeDbContext>().UseNpgsql(ConnectionString).Options;
        return new CafeDbContext(options, new FixedLocation(locationId));
    }

    /// <summary>Creates a second location with its own cashier and a stock row (qty 100) per ingredient.</summary>
    public async Task<(Guid locationId, string email, string password)> CreateOtherLocationAsync(string role = Roles.Cashier)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var loc = new Location { Id = Guid.NewGuid(), Name = "Branch " + Guid.NewGuid().ToString("N")[..6], VatRatePercent = 12m };
        db.Locations.Add(loc);
        await db.SaveChangesAsync();
        var email = $"user-{Guid.NewGuid():N}@cafe.local";
        const string password = "Other#Test12345";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Other User" };
        (await users.CreateAsync(user, password)).Succeeded.ShouldBe(true);
        db.StaffAssignments.Add(new StaffAssignment { UserId = user.Id, LocationId = loc.Id, Role = role });
        foreach (var ing in await db.Ingredients.ToListAsync())
            db.IngredientStocks.Add(new IngredientStock { LocationId = loc.Id, IngredientId = ing.Id, QuantityOnHand = 100 });
        await db.SaveChangesAsync();
        return (loc.Id, email, password);
    }

    public async Task<Guid> VariationIdAsync(HttpClient client, string sku)
    {
        var page = await client.GetFromJsonAsync<PagedResult<ItemDto>>("/api/v1/items?pageSize=100", Json);
        return page!.Data.SelectMany(i => i.Variations).First(v => v.Sku == sku).Id;
    }
}

/// <summary>One shared PostgreSQL container for the whole test run; reaped by Testcontainers (Ryuk) when the process ends.</summary>
internal static class PostgresContainer
{
    private static readonly Lazy<PostgreSqlContainer> Container = new(() =>
    {
        // Testcontainers 4.x negotiates Docker API 1.44, which engines older than Docker 25 reject
        // ("client version 1.44 is too new"). Pin an older, universally supported API version unless the caller chose one.
        if (Environment.GetEnvironmentVariable("DOCKER_API_VERSION") is null)
            Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.43");
        var c = new PostgreSqlBuilder("postgres:16-alpine").Build();
        c.StartAsync().GetAwaiter().GetResult();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { c.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } };
        return c;
    });

    public static string ConnectionString => Container.Value.GetConnectionString();
}

public sealed class FixedLocation : ICurrentLocation
{
    public FixedLocation(Guid? locationId) => LocationId = locationId;
    public Guid? LocationId { get; }
    public Guid? UserId => null;
    public bool IsOwner => false;
}

public static class TestExtensions
{
    public static void ShouldBe<T>(this T actual, T expected) => Assert.Equal(expected, actual);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;

    public static async Task<HttpResponseMessage> PostWithKeyAsync(this HttpClient c, string url, object body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return await c.SendAsync(req);
    }

    public static async Task<int> ProblemStatusAsync(this HttpResponseMessage res)
    {
        res.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("status").GetInt32();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }
