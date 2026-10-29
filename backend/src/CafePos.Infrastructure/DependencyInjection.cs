using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence (DbContext + unit of work). The connection string is read lazily from the container's IConfiguration
    /// (not captured at registration) so test hosts and environment overrides applied after Program starts are honored.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<CafeDbContext>((sp, o) =>
            o.UseNpgsql(
                sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."),
                npgsql => npgsql.MigrationsAssembly(typeof(CafeDbContext).Assembly.GetName().Name)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CafeDbContext>());
        return services;
    }
}
