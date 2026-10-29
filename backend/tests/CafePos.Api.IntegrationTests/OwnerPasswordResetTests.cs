using System.Net;
using System.Net.Http.Json;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CafePos.Api.IntegrationTests;

/// <summary>One-time owner password recovery from Bootstrap:ResetOwnerEmail / ResetOwnerPassword.</summary>
public class OwnerPasswordResetTests
{
    private const string OwnerEmail = DevDataSeeder.AdminEmail;
    private const string OldPassword = DevDataSeeder.AdminPassword;
    private const string NewPassword = "Rec0very#Pass77";

    private static Dictionary<string, string?> Reset(string email, string? password) => new()
    {
        ["Bootstrap:ResetOwnerEmail"] = email, ["Bootstrap:ResetOwnerPassword"] = password
    };

    private static string RefreshCookie(HttpResponseMessage login) =>
        login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal)).Split(';')[0];

    private static async Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private static async Task<HttpStatusCode> Refresh(ApiFactory f, string cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", cookie);
        return (await f.CreateClient(new() { HandleCookies = false }).SendAsync(req)).StatusCode;
    }

    /// <summary>A seeded database (owner has OldPassword); the returned factory must stay alive because disposing it drops the database.</summary>
    private static (ApiFactory first, string db) Seeded()
    {
        var db = "cafepos_test_" + Guid.NewGuid().ToString("N");
        var first = new ApiFactory(db, true);
        _ = first.Services;
        return (first, db);
    }

    private static ApiFactory Restart(string db, Dictionary<string, string?> config) => new(db, false, config);

    [Fact]
    public async Task Resets_the_owner_password_so_the_new_one_works_and_the_old_one_does_not()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using var app = Restart(db, Reset(OwnerEmail.ToUpperInvariant(), NewPassword));
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Login(client, OwnerEmail, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, OwnerEmail, OldPassword)).StatusCode);
    }

    [Fact]
    public async Task A_locked_out_owner_is_unlocked_and_can_log_in_with_the_new_password()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using (var d = first.NewDb(null))
            await d.Users.Where(u => u.Email == OwnerEmail).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddHours(1)).SetProperty(u => u.AccessFailedCount, 3)
                .SetProperty(u => u.ClockPinFailedCount, 4).SetProperty(u => u.ClockPinLockedUntil, DateTime.UtcNow.AddHours(1)));
        using var app = Restart(db, Reset(OwnerEmail, NewPassword));

        Assert.Equal(HttpStatusCode.OK, (await Login(app.CreateClient(), OwnerEmail, NewPassword)).StatusCode);
        using var d2 = app.NewDb(null);
        var user = await d2.Users.AsNoTracking().SingleAsync(u => u.Email == OwnerEmail);
        Assert.Null(user.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.Equal(0, user.ClockPinFailedCount);
        Assert.Null(user.ClockPinLockedUntil);
    }

    [Fact]
    public async Task Existing_refresh_tokens_of_the_owner_are_revoked_but_other_users_keep_theirs()
    {
        var (first, db) = Seeded();
        using var keep = first;
        var ownerCookie = RefreshCookie(await Login(first.CreateClient(), OwnerEmail, OldPassword));
        var cashierCookie = RefreshCookie(await Login(first.CreateClient(), DevDataSeeder.CashierEmail, DevDataSeeder.CashierPassword));

        using var app = Restart(db, Reset(OwnerEmail, NewPassword));
        _ = app.Services;

        Assert.Equal(HttpStatusCode.Unauthorized, await Refresh(app, ownerCookie));
        Assert.Equal(HttpStatusCode.OK, await Refresh(app, cashierCookie));
    }

    public static TheoryData<string, string> Skipped => new()
    {
        { DevDataSeeder.CashierEmail, NewPassword },   // not an owner
        { "nobody@example.com", NewPassword },         // unknown
        { OwnerEmail, "short" }                        // weak password
    };

    [Theory]
    [MemberData(nameof(Skipped))]
    public async Task Invalid_targets_or_passwords_change_nothing_and_the_app_still_starts(string email, string password)
    {
        var (first, db) = Seeded();
        using var keep = first;
        var cookie = RefreshCookie(await Login(first.CreateClient(), OwnerEmail, OldPassword));
        using var app = Restart(db, Reset(email, password));
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, OwnerEmail, OldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, OwnerEmail, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, DevDataSeeder.CashierEmail, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Refresh(app, cookie));
    }

    [Fact]
    public async Task A_deactivated_owner_is_not_reactivated_or_changed()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using (var d = first.NewDb(null))
            await d.Users.Where(u => u.Email == OwnerEmail).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false));
        using var app = Restart(db, Reset(OwnerEmail, NewPassword));
        _ = app.Services;

        using var d2 = app.NewDb(null);
        var user = await d2.Users.AsNoTracking().SingleAsync(u => u.Email == OwnerEmail);
        Assert.False(user.Active);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(app.CreateClient(), OwnerEmail, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task A_second_start_with_the_same_config_is_a_no_op_that_keeps_new_sessions()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using var firstReset = Restart(db, Reset(OwnerEmail, NewPassword));
        var cookie = RefreshCookie(await Login(firstReset.CreateClient(), OwnerEmail, NewPassword));

        using var second = Restart(db, Reset(OwnerEmail, NewPassword));
        _ = second.Services;

        Assert.Equal(HttpStatusCode.OK, await Refresh(second, cookie));
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task A_later_start_with_the_keys_still_set_only_warns_to_remove_them()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using var app = Restart(db, Reset(OwnerEmail, NewPassword));
        _ = app.Services; // first reset happens here
        var logger = new ListLogger();

        using (var scope = app.Services.CreateScope())
            await OwnerPasswordReset.RunAsync(scope.ServiceProvider, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("remove them now", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ResetOwnerEmail")]
    [InlineData("ResetOwnerPassword")]
    public async Task Only_one_key_set_warns_and_does_nothing(string present)
    {
        var (first, db) = Seeded();
        using var keep = first;
        var config = present == "ResetOwnerEmail"
            ? new Dictionary<string, string?> { ["Bootstrap:ResetOwnerEmail"] = OwnerEmail }
            : new Dictionary<string, string?> { ["Bootstrap:ResetOwnerPassword"] = NewPassword };
        using var app = Restart(db, config);
        var client = app.CreateClient();
        var logger = new ListLogger();

        using (var scope = app.Services.CreateScope())
            await OwnerPasswordReset.RunAsync(scope.ServiceProvider, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("must both be set", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, entry.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, OwnerEmail, OldPassword)).StatusCode);
    }

    [Fact]
    public async Task The_success_log_is_a_masked_warning_without_the_password()
    {
        var (first, db) = Seeded();
        using var keep = first;
        using var app = Restart(db, new Dictionary<string, string?>()); // booted without reset config
        _ = app.Services;
        var logger = new ListLogger();

        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        options.ResetOwnerEmail = OwnerEmail;
        options.ResetOwnerPassword = NewPassword;
        await OwnerPasswordReset.RunAsync(scope.ServiceProvider, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"Owner password reset for {OwnerBootstrapper.Mask(OwnerEmail)}. Remove Bootstrap__ResetOwnerEmail and Bootstrap__ResetOwnerPassword now.", entry.Message);
        Assert.DoesNotContain(NewPassword, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerEmail, entry.Message, StringComparison.Ordinal);
    }
}
