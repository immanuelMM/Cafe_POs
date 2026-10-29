using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;

namespace CafePos.Api.Tests;

/// <summary>
/// Guards the API contract the frontend generates its types from (backend/openapi/v1.json) and the auth
/// token/cookie shape the frontend relies on (baseline captured before the refactor, docs/plan/baseline/auth-baseline.md).
/// </summary>
[Collection("api")]
public class ContractTests
{
    private readonly ApiFactory _f;
    public ContractTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task OpenApi_document_matches_the_committed_snapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "openapi", "v1.json");
        Assert.True(File.Exists(path), "backend/openapi/v1.json is missing");
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        var res = await _f.CreateClient().GetAsync("/openapi/v1.json");
        res.EnsureSuccessStatusCode();
        var live = JsonNode.Parse(await res.Content.ReadAsStringAsync())!.AsObject();

        // The runtime document lists the request host under "servers"; the build-time one has none.
        live.Remove("servers");
        committed.Remove("servers");

        Assert.True(JsonNode.DeepEquals(committed, live),
            "The API contract changed. If intentional, regenerate the snapshot: " +
            "`dotnet build src/CafePos.Api -p:OpenApiGenerateDocuments=true` (in backend/), commit backend/openapi/v1.json " +
            "and tell the frontend to run its api:types script.");
    }

    [Fact]
    public async Task Access_token_claims_match_the_baseline()
    {
        var client = _f.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.AdminEmail, password = DevDataSeeder.AdminPassword, locationId = DevDataSeeder.MainLocationId });
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<JsonElement>())!;
        var jwt = auth.GetProperty("accessToken").GetString()!;

        var parts = jwt.Split('.');
        using var header = JsonDocument.Parse(Base64Url(parts[0]));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());

        using var payload = JsonDocument.Parse(Base64Url(parts[1]));
        var claims = payload.RootElement;
        const string nsPrefix = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/";
        Assert.Equal(
            new[] { "sub", nsPrefix + "nameidentifier", nsPrefix + "name", nsPrefix + "emailaddress", "role", "jti", "location_id", "exp", "iss", "aud" },
            claims.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Owner", claims.GetProperty("role").GetString());
        Assert.Equal(DevDataSeeder.MainLocationId.ToString(), claims.GetProperty("location_id").GetString());
        Assert.Equal(DevDataSeeder.AdminEmail, claims.GetProperty(nsPrefix + "emailaddress").GetString());
        Assert.Equal("CafePos", claims.GetProperty("iss").GetString());
        Assert.Equal("CafePos.Client", claims.GetProperty("aud").GetString());
        Assert.Equal(claims.GetProperty("sub").GetString(), claims.GetProperty(nsPrefix + "nameidentifier").GetString());

        // Access token lifetime is 30 minutes (baseline).
        var minutes = (DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64()) - DateTimeOffset.UtcNow).TotalMinutes;
        Assert.InRange(minutes, 29, 31);
    }

    [Fact]
    public async Task Owner_session_without_a_location_has_no_location_claim()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.AdminEmail, password = DevDataSeeder.AdminPassword });
        res.EnsureSuccessStatusCode();
        var jwt = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        using var payload = JsonDocument.Parse(Base64Url(jwt.Split('.')[1]));
        Assert.False(payload.RootElement.TryGetProperty("location_id", out _));
    }

    [Fact]
    public async Task Refresh_cookie_attributes_match_the_baseline()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.AdminEmail, password = DevDataSeeder.AdminPassword });
        res.EnsureSuccessStatusCode();
        var setCookie = Assert.Single(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cafepos_refresh=", StringComparison.Ordinal));

        var attrs = setCookie.Split(';').Select(a => a.Trim()).Skip(1).ToList();
        Assert.Contains("path=/api/v1/auth", attrs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", attrs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("httponly", attrs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", attrs, StringComparer.OrdinalIgnoreCase); // Secure follows Request.IsHttps; the test host is http
        var expires = attrs.Single(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];
        var days = (DateTimeOffset.Parse(expires) - DateTimeOffset.UtcNow).TotalDays;
        Assert.InRange(days, 6.9, 7.1);
    }

    private static string Base64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(b));
    }
}
