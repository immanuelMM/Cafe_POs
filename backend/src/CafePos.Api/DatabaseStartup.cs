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

    public static async Task MigrateAndSeedAsync(IServiceProvider services, bool seedDevData, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
                await db.Database.MigrateAsync(ct);
                if (seedDevData)
                    await DevDataSeeder.SeedAsync(scope.ServiceProvider);
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
