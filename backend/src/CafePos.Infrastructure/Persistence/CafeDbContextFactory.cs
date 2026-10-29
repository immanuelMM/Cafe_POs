using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> works with only <c>--project src/CafePos.Infrastructure</c> (no startup project needed).
/// The connection string comes from the ConnectionStrings__Default environment variable; scaffolding migrations never connects,
/// pass it explicitly (or use the environment variable) for <c>database update</c>. No credentials are stored here.
/// </summary>
internal sealed class CafeDbContextFactory : IDesignTimeDbContextFactory<CafeDbContext>
{
    public CafeDbContext CreateDbContext(string[] args)
    {
        var connection = ConnectionStringResolver.Resolve(Environment.GetEnvironmentVariable("ConnectionStrings__Default"), Environment.GetEnvironmentVariable("DATABASE_URL"))
                         ?? "Host=localhost;Database=cafepos_design_time";
        var options = new DbContextOptionsBuilder<CafeDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(CafeDbContext).Assembly.GetName().Name))
            .Options;
        return new CafeDbContext(options);
    }
}
