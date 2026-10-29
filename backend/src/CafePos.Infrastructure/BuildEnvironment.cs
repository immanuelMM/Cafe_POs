using System.Reflection;

namespace CafePos.Infrastructure;

public static class BuildEnvironment
{
    /// <summary>
    /// True while build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server, GetDocument.Insider) runs Program up to
    /// app.Run(). That run has no signing key or database, so startup validation, migrations and seeding are skipped.
    /// </summary>
    public static bool IsOpenApiGeneration { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
