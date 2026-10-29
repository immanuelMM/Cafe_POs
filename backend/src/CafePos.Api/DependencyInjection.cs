using System.Text;
using System.Text.Json.Serialization;
using CafePos.Api.Auth;
using CafePos.Api.Http;
using CafePos.Api.OpenApi;
using CafePos.Api.RateLimiting;
using CafePos.Api.Validation;
using CafePos.Application.Common;
using CafePos.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace CafePos.Api;

public static class DependencyInjection
{
    /// <summary>Registers the web layer: current user, JWT authentication, policies, rate limiting, controllers, errors, CORS and OpenAPI.</summary>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<HttpCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
        services.AddScoped<ICurrentLocation>(sp => sp.GetRequiredService<HttpCurrentUser>());

        // JWT bearer: validation parameters come from the validated JwtOptions (resolved lazily).
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization(Policies.Register);
        services.AddAuthRateLimiting();
        services.AddProxyTrust();

        services.AddControllers(o => o.Filters.Add<FluentValidationFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                // Bodies are bound case-insensitively; a repeated key (also "email" + "Email") is ambiguous, so refuse it with a 400.
                o.JsonSerializerOptions.AllowDuplicateProperties = false;
            });
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
        services.AddCors(o => o.AddPolicy("frontend", p => p
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Idempotent-Replayed")));

        services.AddOpenApi(o =>
        {
            o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            o.AddDocumentTransformer<OpenApiDocumentSetup>();
            o.AddDocumentTransformer<ContractOrderDocumentTransformer>();
            o.AddSchemaTransformer<NumericSchemaTransformer>();
            o.AddSchemaTransformer<ValidatorRulesSchemaTransformer>();
            o.AddOperationTransformer<MultipartUploadOperationTransformer>();
        });
        return services;
    }
}
