using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;

namespace CafePos.Api.IntegrationTests;

/// <summary>
/// Characterization test for the error contract the frontend depends on (status, title, detail, field errors, extensions).
/// A battery of bad requests is replayed and the normalized responses are compared with a committed golden file captured
/// before the Application-layer refactor. Regenerate after an intentional change:
/// UPDATE_PROBLEM_GOLDEN=1 dotnet test --filter ProblemShapeTests (then review the git diff of ProblemShapeGolden.txt).
/// </summary>
[Collection("api")]
public class ProblemShapeTests
{
    private readonly ApiFactory _f;
    public ProblemShapeTests(ApiFactory f) => _f = f;

    private static readonly Guid Id1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly string Main = DevDataSeeder.MainLocationId.ToString();
    private static readonly string Uptown = DevDataSeeder.UptownLocationId.ToString();

    private sealed record Probe(string Who, string Method, string Url, string? Body, string ContentType = "application/json", string? IdempotencyKey = null);

    private static Probe P(string who, string method, string url, string? body = null, string contentType = "application/json", string? key = null) =>
        new(who, method, url, body, contentType, key);

    private static readonly Probe[] Battery =
    [
        P("anon", "POST", "/api/v1/auth/login", "{}"),
        P("anon", "POST", "/api/v1/auth/login", "{\"email\":\"nope\",\"password\":\"x\"}"),
        P("anon", "POST", "/api/v1/auth/login", "{"),
        P("anon", "POST", "/api/v1/auth/login", "{\"email\":\"a@b.c\",\"password\":5}"),
        P("anon", "POST", "/api/v1/auth/login", "hello", "text/plain"),
        P("admin", "POST", "/api/v1/auth/switch-location", "{\"locationId\":\"abc\"}"),
        P("admin", "POST", "/api/v1/categories", "{}"),
        P("admin", "POST", "/api/v1/categories", "{\"name\":\"\"}"),
        P("admin", "POST", "/api/v1/categories", "{\"name\":\"" + new string('x', 201) + "\"}"),
        P("admin", "POST", "/api/v1/items", "{}"),
        P("admin", "POST", "/api/v1/items", $"{{\"categoryId\":\"{Id1}\",\"name\":\"\",\"variations\":[{{\"name\":\"\",\"sku\":\"\",\"price\":-1}}]}}"),
        P("admin", "POST", "/api/v1/items", $"{{\"categoryId\":\"{Id1}\",\"name\":\"n\",\"variations\":[]}}"),
        P("admin", "POST", $"/api/v1/items/{Id1}/variations", "{\"price\":2000000}"),
        P("admin", "POST", "/api/v1/modifier-lists", "{\"name\":\"\",\"minSelections\":60,\"maxSelections\":0,\"modifiers\":[{\"name\":\"\",\"priceDelta\":999999}]}"),
        P("admin", "POST", "/api/v1/modifier-lists", "{\"name\":\"x\",\"minSelections\":3,\"maxSelections\":1}"),
        P("admin", "POST", "/api/v1/ingredients", "{}"),
        P("admin", "POST", "/api/v1/ingredients", "{\"name\":\"a\",\"unit\":\"\",\"lowStockThreshold\":-1,\"initialQuantity\":-2}"),
        P("admin", "POST", "/api/v1/restocks", "{\"quantity\":0}"),
        P("admin", "POST", $"/api/v1/ingredients/{Id1}/adjustments", "{\"note\":\"\"}"),
        P("admin", "POST", $"/api/v1/ingredients/{Id1}/adjustments", "{\"quantityDelta\":0,\"note\":\"n\"}"),
        P("admin", "PUT", $"/api/v1/variations/{Id1}/recipe", "{\"lines\":[{\"quantity\":0}]}"),
        P("admin", "PUT", $"/api/v1/variations/{Id1}/location-overrides/{Main}", "{\"priceOverride\":-5}"),
        P("admin", "PUT", "/api/v1/branding", "{}"),
        P("admin", "PUT", "/api/v1/branding", "{\"brandName\":\"x\",\"primaryColor\":\"red\",\"secondaryColor\":\"zz\",\"tertiaryColor\":\"yy\",\"gradientAngle\":999}"),
        P("admin", "PUT", "/api/v1/branding", "{\"brandName\":\"   \",\"primaryColor\":\"#aabbcc\"}"),
        P("admin", "PUT", "/api/v1/branding", "{\"brandName\":\"x\",\"primaryColor\":\"#aabbcc\",\"tertiaryColor\":\"#112233\"}"),
        P("admin", "PUT", "/api/v1/branding", "{\"brandName\":\"x\",\"primaryColor\":\"#aabbcc\",\"gradient\":true}"),
        P("admin", "PUT", "/api/v1/branding/designs/logo", "{}"),
        P("admin", "PUT", "/api/v1/branding/designs/logo", "{\"document\":[1]}"),
        P("admin", "PUT", "/api/v1/branding/designs/nope", "{\"document\":{}}"),
        P("owner", "POST", "/api/v1/locations", "{}"),
        P("owner", "POST", "/api/v1/locations", "{\"name\":\"x\",\"timeZone\":\"\",\"vatRatePercent\":101}"),
        P("owner", "POST", "/api/v1/locations", "{\"name\":\"x\",\"timeZone\":\"Mars/Base\"}"),
        P("admin", "PUT", $"/api/v1/locations/{Main}", "{\"name\":\"\",\"vatRatePercent\":-1}"),
        P("admin", "POST", $"/api/v1/locations/{Main}/staff", "{}"),
        P("admin", "POST", $"/api/v1/locations/{Main}/staff", "{\"email\":\"bad\",\"role\":\"Cashier\"}"),
        P("admin", "POST", $"/api/v1/locations/{Main}/staff", "{\"email\":\"cashier@cafe.local\",\"role\":\"Nope\"}"),
        P("owner", "POST", "/api/v1/users", "{}"),
        P("owner", "POST", "/api/v1/users", "{\"email\":\"bad\",\"displayName\":\"\",\"password\":\"short\"}"),
        P("owner", "POST", "/api/v1/users", "{\"email\":\"x@y.z\",\"displayName\":\"X\",\"password\":\"alllowercase1\"}"),
        P("owner", "POST", "/api/v1/users", "{\"email\":\"x@y.z\",\"displayName\":\"X\",\"password\":\"Password#12345\"}"),
        P("owner", "POST", "/api/v1/users", $"{{\"email\":\"x@y.z\",\"displayName\":\"X\",\"password\":\"Password#12345\",\"assignments\":[{{\"locationId\":\"{Main}\",\"role\":\"Boss\"}}]}}"),
        P("owner", "PUT", $"/api/v1/users/{Id1}", "{}"),
        P("owner", "PUT", $"/api/v1/users/{Id1}/pin", "{\"pin\":\"12\"}"),
        P("owner", "POST", $"/api/v1/users/{Id1}/locations", "{}"),
        P("cashier", "PUT", "/api/v1/timeclock/my-pin", "{\"pin\":\"abc\"}"),
        P("cashier", "POST", "/api/v1/timeclock/punch", "{}"),
        P("cashier", "POST", "/api/v1/orders", "{}"),
        P("cashier", "POST", "/api/v1/orders", $"{{\"id\":\"{Id1}\",\"items\":[{{\"quantity\":0}}]}}"),
        P("cashier", "POST", "/api/v1/orders", "{\"id\":\"00000000-0000-0000-0000-000000000000\",\"items\":[]}"),
        P("cashier", "POST", $"/api/v1/orders/{Id1}/items", $"{{\"itemVariationId\":\"{Id1}\",\"quantity\":101}}"),
        P("cashier", "POST", $"/api/v1/orders/{Id1}/complete", "{}"),
        P("cashier", "POST", $"/api/v1/orders/{Id1}/complete", "{\"amountTendered\":-1}", key: "k1"),
        P("cashier", "GET", "/api/v1/orders?status=Nope"),
        P("cashier", "GET", "/api/v1/orders?page=abc"),
        P("cashier", "GET", $"/api/v1/orders/{Id1}"),
        P("cashier", "GET", "/api/v1/orders/not-a-guid"),
        P("cashier", "POST", "/api/v1/categories", "{\"name\":\"x\"}"),
        P("cashier", "GET", $"/api/v1/orders/{Id1}/receipt"),
        P("owner", "GET", "/api/v1/orders/" + Id1),
        P("owner", "POST", "/api/v1/orders", $"{{\"id\":\"{Id1}\",\"items\":[{{\"itemVariationId\":\"{Id1}\"}}]}}"),
        P("owner", "POST", "/api/v1/ingredients", "{\"name\":\"ZZ owner\",\"unit\":\"g\",\"initialQuantity\":5}"),
        P("admin", "POST", "/api/v1/ingredients", "{\"name\":\"Espresso Beans\",\"unit\":\"g\"}"),
        P("admin", "POST", "/api/v1/restocks", $"{{\"ingredientId\":\"{Id1}\",\"quantity\":5}}"),
        P("admin", "GET", $"/api/v1/ingredients/{Id1}/movements"),
        P("admin", "GET", $"/api/v1/variations/{Id1}/recipe"),
        P("admin", "GET", $"/api/v1/ingredients?locationId={Id1}"),
        P("manager", "GET", $"/api/v1/ingredients?locationId={Uptown}"),
        P("admin", "PUT", $"/api/v1/locations/{Id1}", "{\"name\":\"n\",\"timeZone\":\"Asia/Manila\"}"),
        P("admin", "POST", $"/api/v1/locations/{Id1}/staff", "{\"email\":\"cashier@cafe.local\",\"role\":\"Cashier\"}"),
        P("admin", "GET", $"/api/v1/users/{Id1}"),
        P("admin", "DELETE", $"/api/v1/users/{Id1}/locations/{Main}"),
        P("admin", "GET", $"/api/v1/items/{Id1}"),
        P("admin", "PUT", $"/api/v1/items/{Id1}/image", "x", "text/plain"),
        P("admin", "PUT", "/api/v1/branding/logo", "x", "text/plain"),
        P("anon", "POST", "/api/v1/auth/refresh"),
        P("anon", "GET", "/api/v1/items"),
    ];

    private async Task<HttpClient> ClientAsync(string who) => who switch
    {
        "admin" => await _f.AdminAsync(),
        "owner" => await _f.OwnerAsync(),
        "cashier" => await _f.CashierAsync(),
        "manager" => await _f.ManagerAsync(DevDataSeeder.MainLocationId),
        _ => _f.CreateClient(),
    };

    [Fact]
    public async Task Error_responses_match_the_golden_file()
    {
        var clients = new Dictionary<string, HttpClient>();
        var sb = new StringBuilder();
        foreach (var p in Battery)
        {
            if (!clients.TryGetValue(p.Who, out var c)) clients[p.Who] = c = await ClientAsync(p.Who);
            var req = new HttpRequestMessage(new HttpMethod(p.Method), p.Url);
            if (p.Body is not null) req.Content = new StringContent(p.Body, Encoding.UTF8, new MediaTypeHeaderValue(p.ContentType));
            if (p.IdempotencyKey is not null) req.Headers.Add("Idempotency-Key", p.IdempotencyKey);
            var res = await c.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            sb.Append($"### {p.Who} {p.Method} {p.Url.Replace(Main, "{main}").Replace(Uptown, "{uptown}")}");
            if (p.Body is not null) sb.Append($"  <= {(p.Body.Length > 120 ? p.Body[..120] + "..." : p.Body)}");
            sb.AppendLine();
            sb.AppendLine($"{(int)res.StatusCode} {res.Content.Headers.ContentType?.MediaType}");
            sb.AppendLine(Normalize(body));
            sb.AppendLine();
        }
        var actual = sb.ToString().Replace("\r\n", "\n");

        if (Environment.GetEnvironmentVariable("UPDATE_PROBLEM_GOLDEN") == "1")
        {
            var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../ProblemShapeGolden.txt"));
            await File.WriteAllTextAsync(src, actual);
            return;
        }
        var golden = Path.Combine(AppContext.BaseDirectory, "ProblemShapeGolden.txt");
        Assert.Equal((await File.ReadAllTextAsync(golden)).Replace("\r\n", "\n"), actual);
    }

    private static string Normalize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty)";
        try
        {
            if (JsonNode.Parse(body) is JsonObject o)
            {
                o.Remove("traceId");
                return Sort(o).ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }
            return body;
        }
        catch (JsonException) { return body; }
    }

    private static JsonObject Sort(JsonObject o)
    {
        var sorted = new JsonObject();
        foreach (var kv in o.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList())
        {
            var v = kv.Value;
            o.Remove(kv.Key);
            sorted.Add(kv.Key, v is JsonObject child ? Sort(child) : v);
        }
        return sorted;
    }
}
