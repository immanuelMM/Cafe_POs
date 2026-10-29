using System.Net;

namespace CafePos.Api.IntegrationTests;

/// <summary>Optional single-service SPA hosting (Spa:RootPath).</summary>
public sealed class SpaHostingTests : IDisposable
{
    private const string IndexHtml = "<!doctype html><html><body>spa-index</body></html>";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cafepos_spa_" + Guid.NewGuid().ToString("N"));

    public SpaHostingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        File.WriteAllText(Path.Combine(_root, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(_root, "assets", "app-abc123.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(_root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static ApiFactory NewFactory(string? spaRoot) =>
        new("cafepos_test_" + Guid.NewGuid().ToString("N"), false,
            spaRoot is null ? null : new Dictionary<string, string?> { ["Spa:RootPath"] = spaRoot });

    [Fact]
    public async Task A_deep_link_returns_index_html_that_must_be_revalidated()
    {
        using var factory = NewFactory(_root);
        var client = factory.CreateClient();

        foreach (var path in new[] { "/", "/orders/123", "/index.html" })
        {
            var res = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(IndexHtml, await res.Content.ReadAsStringAsync());
            Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task Unknown_api_paths_still_return_the_problem_404_not_index_html()
    {
        using var factory = NewFactory(_root);
        var client = factory.CreateClient();

        foreach (var path in new[] { "/api/v1/nope", "/api", "/health/nope", "/openapi/nope.json", "/API/v1/nope" })
        {
            var res = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.NotEqual("text/html", res.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("spa-index", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal(404, await (await client.GetAsync("/api/v1/nope")).ProblemStatusAsync());
    }

    [Fact]
    public async Task Health_openapi_and_real_api_routes_are_unaffected()
    {
        using var factory = NewFactory(_root);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var openapi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        Assert.DoesNotContain("spa-index", await openapi.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/items")).StatusCode);
    }

    [Fact]
    public async Task Hashed_assets_are_immutable_and_other_files_revalidate()
    {
        using var factory = NewFactory(_root);
        var client = factory.CreateClient();

        var asset = await client.GetAsync("/assets/app-abc123.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", asset.Headers.CacheControl?.ToString());

        var icon = await client.GetAsync("/favicon.svg");
        Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("no-cache", icon.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_missing_file_and_non_get_requests_are_404_not_index_html()
    {
        using var factory = NewFactory(_root);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/missing.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/orders/123", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task Spa_routes_do_not_appear_in_the_openapi_document()
    {
        using var plain = NewFactory(null);
        using var spa = NewFactory(_root);

        var without = await plain.CreateClient().GetStringAsync("/openapi/v1.json");
        var with = await spa.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Equal(without, with);
    }

    [Fact]
    public async Task Without_the_setting_nothing_is_served()
    {
        using var factory = NewFactory(null);

        var res = await factory.CreateClient().GetAsync("/orders/123");
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode); // 401 from the secure-by-default fallback policy, exactly as before
    }

    [Fact]
    public async Task A_missing_directory_logs_an_error_and_runs_as_api_only()
    {
        using var factory = NewFactory(Path.Combine(_root, "does-not-exist"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/orders/123")).StatusCode);
    }
}
