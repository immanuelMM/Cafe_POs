using CafePos.Api;
using CafePos.Api.Http;
using CafePos.Api.RateLimiting;
using CafePos.Application;
using CafePos.Infrastructure;
using CafePos.Infrastructure.Auth;
using CafePos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
        await DatabaseStartup.MigrateAndSeedAsync(app.Services, database.SeedDevData, app.Logger);
    // Runs whether or not this process migrated (a pre-deploy step may have), and only when Bootstrap credentials are configured.
    if (app.Services.GetRequiredService<IOptions<BootstrapOptions>>().Value.IsConfigured)
        await DatabaseStartup.BootstrapOwnerAsync(app.Services, app.Logger);
    // One-time password recovery for hosts without a shell; after the bootstrap so a freshly created owner is not reset.
    if (app.Services.GetRequiredService<IOptions<BootstrapOptions>>().Value.IsResetConfigured)
        await DatabaseStartup.ResetOwnerPasswordAsync(app.Services, app.Logger);
}

// First, so every later component (HTTPS redirection, CORS, rate limiting, cookies) sees the client address and scheme set by a
// trusted proxy. Not added when no proxy is configured: an empty trust list would mean "trust everyone".
if (app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value.HasTrustedProxies)
    app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var spaFiles = BuildEnvironment.IsOpenApiGeneration ? null : SpaHosting.Resolve(app.Configuration, app.Environment, app.Logger);
if (spaFiles is not null)
{
    app.UseSpaStaticFiles(spaFiles);
    // Routing is otherwise added implicitly at the very start, and static files skip any request that already has an endpoint
    // (the SPA fallback matches everything). Adding it explicitly here keeps the static files first.
    app.UseRouting();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();                                    // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("Cafe POS API")).AllowAnonymous(); // /scalar/v1
}

app.UseCors("frontend");
app.UseMiddleware<RateLimitKeyCaptureMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Liveness: the process is up (no dependencies). Readiness: the database is reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

if (spaFiles is not null) app.MapSpaFallback(spaFiles);

app.Run();

public partial class Program;
