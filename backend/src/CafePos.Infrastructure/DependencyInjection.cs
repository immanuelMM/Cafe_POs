using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Auth;
using CafePos.Application.Features.Branding;
using CafePos.Application.Features.Catalog;
using CafePos.Application.Features.Inventory;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Orders;
using CafePos.Application.Features.TimeClock;
using CafePos.Application.Features.Users;
using CafePos.Application.Features.VariationOverrides;
using CafePos.Infrastructure.Auth;
using CafePos.Infrastructure.Identity;
using CafePos.Infrastructure.Persistence;
using CafePos.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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

        // Options are validated when the host starts: a missing/short Jwt:Key or missing connection string fails startup.
        var jwt = services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations();
        var database = services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Configure<IConfiguration>((options, config) => options.ConnectionString = config.GetConnectionString("Default") ?? "")
            .ValidateDataAnnotations();
        services.AddOptions<BootstrapOptions>().BindConfiguration(BootstrapOptions.SectionName);
        if (!BuildEnvironment.IsOpenApiGeneration) // build-time OpenAPI generation starts the host without secrets
        {
            jwt.ValidateOnStart();
            database.ValidateOnStart();
        }

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<CafeDbContext>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPinHasher, PinHasher>();

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IModifierListRepository, ModifierListRepository>();
        services.AddScoped<IIngredientRepository, IngredientRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderCatalogRepository, OrderCatalogRepository>();
        services.AddScoped<IBrandingRepository, BrandingRepository>();
        services.AddScoped<ITimeClockRepository, TimeClockRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IStaffAssignmentRepository, StaffAssignmentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IVariationOverrideRepository, VariationOverrideRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        return services;
    }
}
