using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CafePos.Api;

/// <summary>
/// Startup migration/seeding that tolerates a database that is still starting or briefly unreachable (container restarts, a slow
/// Docker host). Without this a single connect timeout crashed the process. Only transient connectivity failures are retried;
/// anything else (a failing migration, a bad connection string) still fails fast. This is deliberately not EF's
/// EnableRetryOnFailure, which would also wrap request-time transactions (the order-completion unit of work).
/// </summary>
public static class DatabaseStartup
{
    private const int MaxAttempts = 8;

    public static Task MigrateAndSeedAsync(IServiceProvider services, bool seedDevData, ILogger logger, CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
            await db.Database.MigrateAsync(ct);
            if (seedDevData)
                await DevDataSeeder.SeedAsync(scope.ServiceProvider);
        }, logger, ct);

    /// <summary>
    /// First-owner bootstrap (see <see cref="OwnerBootstrapper"/>). Independent of migrate-on-startup so it also works when migrations
    /// are applied by a pre-deploy step. It must never stop the host: transient database errors are retried like migrations; anything
    /// else is logged (type and SQLSTATE only, never messages that could carry the email or password) and swallowed.
    /// </summary>
    public static async Task BootstrapOwnerAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await WithRetryAsync(async () =>
            {
                using var scope = services.CreateScope();
                await OwnerBootstrapper.RunAsync(scope.ServiceProvider, logger, ct);
            }, logger, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sqlState = (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState;
            if (sqlState == PostgresErrorCodes.UndefinedTable)
                logger.LogError("Owner bootstrap skipped: the database schema is not migrated yet. Run the migrations (or set Database__MigrateOnStartup=true) and restart. Startup continues.");
            else
                logger.LogError("Owner bootstrap failed with {ExceptionType} (SQLSTATE {SqlState}); startup continues without it.", ex.GetType().Name, sqlState ?? "n/a");
        }
    }

    /// <summary>
    /// One-time owner password recovery (see <see cref="OwnerPasswordReset"/>), same retry and never-crash policy as
    /// <see cref="BootstrapOwnerAsync"/>. Runs after migrations and the bootstrap, independent of migrate-on-startup.
    /// </summary>
    public static async Task ResetOwnerPasswordAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await WithRetryAsync(async () =>
            {
                using var scope = services.CreateScope();
                await OwnerPasswordReset.RunAsync(scope.ServiceProvider, logger, ct);
            }, logger, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sqlState = (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState;
            logger.LogError("Owner password reset failed with {ExceptionType} (SQLSTATE {SqlState}); startup continues and nothing was changed.", ex.GetType().Name, sqlState ?? "n/a");
        }
    }

    private static async Task WithRetryAsync(Func<Task> action, ILogger logger, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}); retrying in {Delay}s.", attempt, MaxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException or System.Net.Sockets.SocketException or IOException
        || ex is NpgsqlException { IsTransient: true }
        || (ex.InnerException is not null && IsTransient(ex.InnerException));
}
