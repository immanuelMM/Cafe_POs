using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CafePos.Infrastructure.Persistence;

/// <summary>Readiness check: the database accepts connections.</summary>
internal sealed class DatabaseHealthCheck(CafeDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}
