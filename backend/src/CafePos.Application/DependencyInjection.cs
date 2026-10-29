using CafePos.Application.Features.Branding;
using CafePos.Application.Features.Catalog;
using CafePos.Application.Features.Inventory;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Orders;
using CafePos.Application.Features.TimeClock;
using CafePos.Application.Features.Users;
using CafePos.Application.Features.VariationOverrides;
using CafePos.Application.Payments;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // Request validators are stateless; the Api layer resolves them by request type (validation filter, OpenAPI schema rules).
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton, includeInternalTypes: true);

        services.AddScoped<CategoryService>();
        services.AddScoped<ItemService>();
        services.AddScoped<ModifierListService>();
        services.AddScoped<IngredientService>();
        services.AddScoped<RecipeService>();
        services.AddScoped<CafePos.Application.Features.Inventory.StockService>();
        services.AddScoped<OrderService>();
        services.AddSingleton<IPaymentProvider, CashPaymentProvider>();
        services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();
        services.AddScoped<BrandingService>();
        services.AddScoped<TimeClockService>();
        services.AddScoped<LocationService>();
        services.AddScoped<UserService>();
        services.AddScoped<VariationOverrideService>();
        return services;
    }
}
