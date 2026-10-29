using CafePos.Api;
using CafePos.Api.RateLimiting;
using CafePos.Application;
using CafePos.Infrastructure;
using CafePos.Infrastructure.Auth;
using CafePos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Each layer registers itself; configuration is read lazily (IOptions) so test hosts and environment overrides are honored.
builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddApi(builder.Configuration);

var app = builder.Build();

// Build-time OpenAPI generation runs this file up to app.Run(); it must not need a signing key or touch the database.
if (!BuildEnvironment.IsOpenApiGeneration)
{
    // Fail fast on a bad configuration (for example Jwt:Key shorter than 32 characters) before touching the database.
    _ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
    var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (database.MigrateOnStartup)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
        await db.Database.MigrateAsync();
        if (database.SeedDevData)
            await DevDataSeeder.SeedAsync(scope.ServiceProvider);
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();                                    // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("Cafe POS API")).AllowAnonymous(); // /scalar/v1
}

app.UseCors("frontend");
app.UseMiddleware<LoginEmailCaptureMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program;
