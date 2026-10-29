using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Domain;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CafePos.Api.Tests;

/// <summary>
/// Refactor safety net (plan risk R2): a database created by the PRE-refactor build (the M3 commit, single CafePos.Api project,
/// namespaces CafePos.Api.Data / CafePos.Api.Domain, all six migrations applied, with data) must be usable by the refactored build
/// as is: no migration is applied or re-applied, the schema is untouched, the model has no pending changes, the app boots and
/// the old data and password hashes work.
/// The dump (Fixtures/pre-refactor-m3.sql) was produced by running the M3 build against an empty PostgreSQL 16 database
/// (migrate + dev seed), then logging in, creating and completing an order, saving branding and a design, setting a time clock PIN,
/// punching in and adding a price override, and running pg_dump --inserts --no-owner.
/// </summary>
public class UpgradeFromPreRefactorTests
{
    private const string AdditiveMigration = "RefreshTokenReplacementAndIp";

    private static readonly string[] ExpectedMigrations =
    [
        "20261016132257_InitialCreate",
        "20261018040804_Phase2MultiLocation",
        "20261018052130_BrandSettings",
        "20261018053956_BrandingAssetsAndTimeClock",
        "20261018060644_ItemPhotos",
        "20261018062510_BrandColorsAndDesigns"
    ];

    [Fact]
    public async Task A_pre_refactor_database_is_usable_as_is_by_the_refactored_build()
    {
        var dbName = "cafepos_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(ApiFactory.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        using var factory = new ApiFactory(dbName, seedDevData: false);
        var dbConnection = factory.ConnectionString;

        // 1. Restore the dump made by the pre-refactor build.
        var dump = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pre-refactor-m3.sql"), Encoding.UTF8);
        await using (var conn = new NpgsqlConnection(dbConnection))
        {
            await conn.OpenAsync();
            await using var restore = new NpgsqlCommand(dump, conn);
            await restore.ExecuteNonQueryAsync();
        }

        var historyBefore = await ReadAsync(dbConnection, "SELECT \"MigrationId\", \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
        var schemaBefore = await SchemaFingerprintAsync(dbConnection);
        Assert.Equal(ExpectedMigrations, historyBefore.Select(r => r.Split('|')[0]).ToArray());

        // 2. Boot the refactored app on it. Database:MigrateOnStartup=true runs Migrate(); nothing may be pending.
        var client = factory.CreateClient(new() { HandleCookies = true });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges(), "The EF model differs from the migration snapshot; add a migration (see plan M6).");
        }

        // 3. The six pre-refactor migrations are untouched. Since the refactor exactly ONE additive migration exists (plan C5: two
        //    nullable RefreshTokens columns); the boot applied it, and nothing else in the schema changed.
        var historyAfter = await ReadAsync(dbConnection, "SELECT \"MigrationId\", \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
        Assert.Equal(historyBefore, historyAfter.Take(historyBefore.Count).ToList());
        var applied = Assert.Single(historyAfter.Skip(historyBefore.Count));
        Assert.EndsWith("_" + AdditiveMigration, applied.Split('|')[0]);

        var schemaAfter = await SchemaFingerprintAsync(dbConnection);
        Assert.Empty(schemaBefore.Except(schemaAfter));
        var added = schemaAfter.Except(schemaBefore).ToList();
        Assert.Equal(2, added.Count);
        Assert.Contains(added, l => l.StartsWith("RefreshTokens|CreatedByIp|", StringComparison.Ordinal));
        Assert.Contains(added, l => l.StartsWith("RefreshTokens|ReplacedByTokenHash|", StringComparison.Ordinal));

        // 4. Old data and old password hashes work through the API.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<JsonElement>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var orders = await client.GetFromJsonAsync<JsonElement>("/api/v1/orders", ApiFactory.Json);
        var order = orders.GetProperty("data").EnumerateArray().Single();
        Assert.Equal("Completed", order.GetProperty("status").GetString());
        Assert.Equal(1, order.GetProperty("orderNumber").GetInt32());
        var receipt = await client.GetAsync($"/api/v1/orders/{order.GetProperty("id").GetGuid()}/receipt");
        receipt.EnsureSuccessStatusCode();

        var branding = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/branding", ApiFactory.Json);
        Assert.Equal("Pre Refactor Cafe", branding.GetProperty("brandName").GetString());

        var staff = await client.GetFromJsonAsync<JsonElement>("/api/v1/timeclock/staff", ApiFactory.Json);
        Assert.Contains(staff.EnumerateArray(), s => s.GetProperty("clockedIn").GetBoolean());

        // The old stock ledger and decremented stock survive; new work keeps functioning on the restored database.
        var ingredients = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredients", ApiFactory.Json);
        Assert.Contains(ingredients.EnumerateArray(), i => i.GetProperty("name").GetString() == "Espresso Beans" && i.GetProperty("quantityOnHand").GetDecimal() < 5000m);
        var itemsPage = await client.GetFromJsonAsync<PagedResult<JsonElement>>("/api/v1/items?pageSize=100", ApiFactory.Json);
        Assert.NotEmpty(itemsPage!.Data);
    }

    private static async Task<List<string>> ReadAsync(string connectionString, string sql)
    {
        var rows = new List<string>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows;
    }

    /// <summary>Columns, indexes and constraints of the public schema, so any schema change shows up as a difference.</summary>
    private static async Task<List<string>> SchemaFingerprintAsync(string connectionString)
    {
        var columns = await ReadAsync(connectionString,
            "SELECT table_name, column_name, data_type, is_nullable, character_maximum_length, numeric_precision, numeric_scale, column_default " +
            "FROM information_schema.columns WHERE table_schema = 'public' ORDER BY table_name, column_name");
        var indexes = await ReadAsync(connectionString,
            "SELECT tablename, indexname, indexdef FROM pg_indexes WHERE schemaname = 'public' ORDER BY tablename, indexname");
        var constraints = await ReadAsync(connectionString,
            "SELECT conrelid::regclass::text, conname, pg_get_constraintdef(oid) FROM pg_constraint " +
            "WHERE connamespace = 'public'::regnamespace ORDER BY 1, 2");
        return columns.Concat(indexes).Concat(constraints).ToList();
    }
}
