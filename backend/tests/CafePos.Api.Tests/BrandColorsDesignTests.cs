using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CafePos.Api.Dtos;

namespace CafePos.Api.Tests;

[Collection("api")]
public class BrandColorsDesignTests
{
    private readonly ApiFactory _f;
    public BrandColorsDesignTests(ApiFactory f) => _f = f;

    private static object Body(string? secondary = null, string? tertiary = null, bool gradient = false, int angle = 135) => new
    {
        brandName = "Colour Test", primaryColor = "#3B2417", secondaryColor = secondary, tertiaryColor = tertiary, gradient, gradientAngle = angle
    };

    [Fact]
    public async Task Three_color_gradient_roundtrips_and_rules_are_enforced()
    {
        var admin = await _f.AdminAsync();
        var res = await admin.PutAsJsonAsync("/api/v1/branding", Body("#B4561F", "#F2C48D", gradient: true, angle: 90));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var anon = (await _f.CreateClient().GetFromJsonAsync<BrandingDto>("/api/v1/branding", ApiFactory.Json))!;
        Assert.Equal(("#3b2417", "#b4561f", "#f2c48d", true, 90), (anon.PrimaryColor, anon.SecondaryColor, anon.TertiaryColor, anon.Gradient, anon.GradientAngle));

        (await admin.PutAsJsonAsync("/api/v1/branding", Body(null, "#f2c48d"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // tertiary without secondary
        (await admin.PutAsJsonAsync("/api/v1/branding", Body(gradient: true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);     // gradient needs 2 colors
        (await admin.PutAsJsonAsync("/api/v1/branding", Body("blue"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);             // bad hex
        (await admin.PutAsJsonAsync("/api/v1/branding", Body("#b4561f", angle: 400))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Empty strings clear the optional colors; solid mode is the default.
        var cleared = await admin.PutAsJsonAsync("/api/v1/branding", Body("", ""));
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await cleared.Content.ReadFromJsonAsync<BrandingDto>(ApiFactory.Json))!;
        Assert.Null(dto.SecondaryColor);
        Assert.Null(dto.TertiaryColor);
        Assert.False(dto.Gradient);
    }

    [Fact]
    public async Task Designs_save_and_load_per_kind_and_are_admin_only()
    {
        var admin = await _f.AdminAsync();
        var doc = new { version = 1, width = 512, height = 512, layers = new object[] { new { type = "text", text = "M", x = 10, y = 20 } } };
        var put = await admin.PutAsJsonAsync("/api/v1/branding/designs/logo", new { document = doc });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var got = (await admin.GetFromJsonAsync<BrandDesignDto>("/api/v1/branding/designs/logo", ApiFactory.Json))!;
        Assert.Equal("logo", got.Kind);
        Assert.Equal(512, got.Document.GetProperty("width").GetInt32());
        Assert.Equal("M", got.Document.GetProperty("layers")[0].GetProperty("text").GetString());

        (await admin.PutAsJsonAsync("/api/v1/branding/designs/banner", new { document = new[] { 1, 2 } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // not an object
        (await admin.GetAsync("/api/v1/branding/designs/poster")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var cashier = await _f.CashierAsync();
        (await cashier.GetAsync("/api/v1/branding/designs/logo")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _f.CreateClient().GetAsync("/api/v1/branding/designs/logo")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
