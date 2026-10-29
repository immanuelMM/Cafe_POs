using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Api.Http;
using CafePos.Api.Infrastructure;
using CafePos.Api.OpenApi;
using CafePos.Api.Validation;
using CafePos.Application;
using CafePos.Infrastructure;
using CafePos.Api.Payments;
using CafePos.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
// NOTE: read configuration lazily (via IOptions / IConfiguration from DI) so that test hosts and
// environment overrides applied after this point are honored.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// ---- Data ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddScoped<ICurrentLocation>(sp => sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// ---- Identity + JWT ----
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<CafeDbContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        o.TokenValidationParameters = new TokenValidationParameters
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
builder.Services.AddAuthorization(Policies.Register);
builder.Services.AddSingleton<IAuthorizationHandler, LocationAccessHandler>();
builder.Services.AddScoped<ILocationAccess, LocationAccess>();

// ---- App services ----
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<IPaymentProvider, CashPaymentProvider>();
builder.Services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();

// ---- Web ----
builder.Services.AddControllers(o => o.Filters.Add<FluentValidationFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Idempotent-Replayed")));

builder.Services.AddOpenApi(o =>
{
    o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
    o.AddDocumentTransformer<OpenApiDocumentSetup>();
    o.AddSchemaTransformer<NumericSchemaTransformer>();
    o.AddSchemaTransformer<ValidatorRulesSchemaTransformer>();
    o.AddOperationTransformer<MultipartUploadOperationTransformer>();
});

var app = builder.Build();

// Build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server / GetDocument.Insider) runs this file up to
// app.Run(); it must not need a signing key or touch the database.
var isBuildTimeGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!isBuildTimeGeneration)
{
    var jwtKey = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.Key;
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        throw new InvalidOperationException("Jwt:Key must be set (min 32 characters). Supply it via the Jwt__Key environment variable or user-secrets; it is never committed.");
}

if (!isBuildTimeGeneration && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Database:SeedDevData"))
        await DevDataSeeder.SeedAsync(scope.ServiceProvider);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();                                    // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("Cafe POS API")).AllowAnonymous(); // /scalar/v1
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program;
