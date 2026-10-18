using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CafePos.Api.Tests;

internal static class AdvHelpers
{
    public static readonly Guid Main = DevDataSeeder.MainLocationId;
    public static readonly Guid Uptown = DevDataSeeder.UptownLocationId;

    public static string RefreshCookieValue(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("cafepos_refresh=")).Split(';')[0];

    public static HttpClient WithToken(ApiFactory f, string token)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>Re-signs the claims of a real token with a different key (tampering simulation).</summary>
    public static string Resign(string realToken, string key, Func<IEnumerable<System.Security.Claims.Claim>, IEnumerable<System.Security.Claims.Claim>> edit)
    {
        var old = new JwtSecurityTokenHandler().ReadJwtToken(realToken);
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(old.Issuer, old.Audiences.First(), edit(old.Claims), expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public static Task<HttpResponseMessage> Complete(HttpClient c, Guid orderId, string key) =>
        c.PostWithKeyAsync($"/api/v1/orders/{orderId}/complete", new { paymentMethod = "cash", amountTendered = 10000 }, key);

    public static async Task<Guid> CreateUserAsync(HttpClient owner, string role, params Guid[] locations)
    {
        var email = $"adv-{Guid.NewGuid():N}@cafe.local";
        var res = await owner.PostAsJsonAsync("/api/v1/users", new
        {
            email, displayName = "Adv " + role, password = "AdvUser#12345",
            assignments = locations.Select(l => new { locationId = l, role }).ToArray()
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.ReadAsync<UserDto>();
        Emails[dto.Id] = email;
        return dto.Id;
    }

    public static readonly Dictionary<Guid, string> Emails = new();
    public const string Password = "AdvUser#12345";

    public static async Task<Guid> OwnerIdAsync(HttpClient owner) =>
        (await owner.GetFromJsonAsync<UserInfoDto>("/api/v1/auth/me", ApiFactory.Json))!.Id;
}

[Collection("api")]
public class OrderCrossLocationAdversarialTests
{
    private readonly ApiFactory _f;
    public OrderCrossLocationAdversarialTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Other_location_staff_cannot_read_or_mutate_an_open_order_by_id_and_it_stays_untouched()
    {
        var main = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(main, "CRO-1");
        var order = await Phase2Helpers.OrderAsync(main, cro, 1);
        var itemId = order.Items.Single().Id;

        var attackers = new[] { await _f.UptownCashierAsync(), await _f.ManagerAsync(AdvHelpers.Uptown) };
        foreach (var a in attackers)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/orders/{order.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/orders/{order.Id}/receipt")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/v1/orders/{order.Id}/items", new { itemVariationId = cro, quantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync($"/api/v1/orders/{order.Id}/items/{itemId}", new { itemVariationId = cro, quantity = 5 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/api/v1/orders/{order.Id}/items/{itemId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await AdvHelpers.Complete(a, order.Id, Guid.NewGuid().ToString())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/api/v1/orders/{order.Id}/cancel", null)).StatusCode);
        }

        var after = await main.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json);
        Assert.Equal("Open", after!.Status);
        Assert.Equal(order.Total, after.Total);
        Assert.Single(after.Items);
        Assert.Equal(itemId, after.Items.Single().Id);
    }

    [Fact]
    public async Task Other_location_cannot_read_a_completed_order_receipt_or_replay_its_idempotency_key()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var order = await Phase2Helpers.OrderAsync(main, await _f.VariationIdAsync(main, "CRO-1"));
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await AdvHelpers.Complete(main, order.Id, key)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await uptown.GetAsync($"/api/v1/orders/{order.Id}/receipt")).StatusCode);
        var replay = await AdvHelpers.Complete(uptown, order.Id, key);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode); // must not leak the stored response
        Assert.DoesNotContain(order.Id.ToString(), await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Same_idempotency_key_at_two_locations_for_different_orders_does_not_collide()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var o1 = await Phase2Helpers.OrderAsync(main, await _f.VariationIdAsync(main, "CRO-1"));
        var o2 = await Phase2Helpers.OrderAsync(uptown, await _f.VariationIdAsync(uptown, "CRO-1"));
        var key = "shared-" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await AdvHelpers.Complete(main, o1.Id, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdvHelpers.Complete(uptown, o2.Id, key)).StatusCode);
    }

    [Fact]
    public async Task Creating_an_order_with_an_id_owned_by_another_location_is_rejected_and_does_not_leak_it()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var cro = await _f.VariationIdAsync(main, "CRO-1");
        var order = await Phase2Helpers.OrderAsync(main, cro, 3);

        var res = await uptown.PostAsJsonAsync("/api/v1/orders", new { id = order.Id, items = new[] { new { itemVariationId = cro, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.DoesNotContain(order.CashierName, await res.Content.ReadAsStringAsync());
        var still = await main.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json);
        Assert.Equal(AdvHelpers.Main, still!.LocationId);
        Assert.Equal(3, still.Items.Single().Quantity);
    }

    [Fact]
    public async Task Order_lists_never_include_other_locations_for_non_owners()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        await Phase2Helpers.OrderAsync(main, await _f.VariationIdAsync(main, "CRO-1"));
        await Phase2Helpers.OrderAsync(uptown, await _f.VariationIdAsync(uptown, "CRO-1"));
        var mgr = await _f.ManagerAsync(AdvHelpers.Main);

        var page = await mgr.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders?pageSize=100", ApiFactory.Json);
        Assert.All(page!.Data, o => Assert.Equal(AdvHelpers.Main, o.LocationId));
        var cashierPage = await uptown.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders?pageSize=100", ApiFactory.Json);
        Assert.All(cashierPage!.Data, o => Assert.Equal(AdvHelpers.Uptown, o.LocationId));
    }

    [Fact]
    public async Task Owner_with_an_active_location_and_no_filter_sees_only_that_location_and_unknown_location_is_404()
    {
        var uptown = await _f.UptownCashierAsync();
        var uo = await Phase2Helpers.OrderAsync(uptown, await _f.VariationIdAsync(uptown, "CRO-1"));
        var owner = await _f.AdminAsync(); // Owner at Main
        var page = await owner.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders?pageSize=100", ApiFactory.Json);
        Assert.All(page!.Data, o => Assert.Equal(AdvHelpers.Main, o.LocationId));
        Assert.DoesNotContain(page.Data, o => o.Id == uo.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/orders?locationId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Owner_at_one_location_cannot_mutate_another_locations_order()
    {
        var uptown = await _f.UptownCashierAsync();
        var cro = await _f.VariationIdAsync(uptown, "CRO-1");
        var uo = await Phase2Helpers.OrderAsync(uptown, cro);
        var owner = await _f.AdminAsync(); // Owner at Main
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/v1/orders/{uo.Id}/items", new { itemVariationId = cro, quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AdvHelpers.Complete(owner, uo.Id, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/v1/orders/{uo.Id}/cancel", null)).StatusCode);
        Assert.Equal("Open", (await uptown.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{uo.Id}", ApiFactory.Json))!.Status);
    }

    [Fact(Skip = "BUG: POST /orders in an Owner cross-location session returns 500 instead of the documented 400 'Location required'. Suspect OrderService.CreateAsync: `FirstAsync(l => l.Id == LocationId)` evaluates the throwing LocationId property inside the EF expression, so the ApiException is wrapped/unhandled.")]
    public async Task Owner_cross_location_session_create_order_is_400_location_required()
    {
        var owner = await _f.OwnerAsync();
        var cro = await _f.VariationIdAsync(owner, "CRO-1");
        var create = await owner.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { new { itemVariationId = cro, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("Location required", await create.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_cross_location_session_cannot_write_location_scoped_stock_data()
    {
        var owner = await _f.OwnerAsync();
        var beans = (await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients?locationId={AdvHelpers.Main}", ApiFactory.Json))!.First(i => i.Name == "Espresso Beans").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/restocks", new { ingredientId = beans, quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/ingredients/{beans}/adjustments", new { quantityDelta = -1, reason = "Waste", note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/locations/current")).StatusCode);

        // Ingredient with an opening balance needs a location; the ingredient must not be created as a side effect.
        var name = "NoLoc-" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/ingredients", new { name, unit = "g", lowStockThreshold = 1, initialQuantity = 5 })).StatusCode);
        var all = await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients?locationId={AdvHelpers.Main}&includeInactive=true", ApiFactory.Json);
        Assert.DoesNotContain(all!, i => i.Name == name);
    }

    [Fact]
    public async Task Owner_cross_location_session_cannot_complete_or_edit_existing_orders()
    {
        var main = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(main, "CRO-1");
        var order = await Phase2Helpers.OrderAsync(main, cro);
        var owner = await _f.OwnerAsync();
        var complete = await AdvHelpers.Complete(owner, order.Id, Guid.NewGuid().ToString());
        Assert.Contains((int)complete.StatusCode, new[] { 400, 404 });
        var add = await owner.PostAsJsonAsync($"/api/v1/orders/{order.Id}/items", new { itemVariationId = cro, quantity = 1 });
        Assert.Contains((int)add.StatusCode, new[] { 400, 404 });
        Assert.Equal("Open", (await main.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json))!.Status);
    }
}

[Collection("api")]
public class LocationIdParameterAdversarialTests
{
    private readonly ApiFactory _f;
    public LocationIdParameterAdversarialTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Non_owner_passing_another_locations_id_is_403_on_every_read_endpoint()
    {
        var beans = await Phase2Helpers.IngredientIdAsync(await _f.AdminAsync(), "Espresso Beans");
        var mgr = await _f.ManagerAsync(AdvHelpers.Main); // also assigned to Uptown, but not active there
        var cashier = await _f.CashierAsync();
        var urls = new[]
        {
            $"/api/v1/orders?locationId={AdvHelpers.Uptown}",
            $"/api/v1/ingredients?locationId={AdvHelpers.Uptown}",
            $"/api/v1/ingredients/low-stock?locationId={AdvHelpers.Uptown}",
        };
        foreach (var u in urls)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync(u)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(u)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync($"/api/v1/ingredients/{beans}/movements?locationId={AdvHelpers.Uptown}")).StatusCode);
        // Cashiers are below Manager for the ledger regardless of locationId.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/ingredients/{beans}/movements")).StatusCode);
        // A random (nonexistent) location id is also 403 for non-owners, not 404 (no existence probing).
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync($"/api/v1/orders?locationId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Owner_may_use_locationId_on_every_read_endpoint()
    {
        var owner = await _f.AdminAsync();
        var beans = await Phase2Helpers.IngredientIdAsync(owner, "Espresso Beans");
        foreach (var u in new[]
        {
            $"/api/v1/orders?locationId={AdvHelpers.Uptown}",
            $"/api/v1/ingredients?locationId={AdvHelpers.Uptown}",
            $"/api/v1/ingredients/low-stock?locationId={AdvHelpers.Uptown}",
            $"/api/v1/ingredients/{beans}/movements?locationId={AdvHelpers.Uptown}",
        })
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(u)).StatusCode);
    }

    [Fact]
    public async Task Stock_writes_ignore_a_client_sent_locationId_in_body_or_query()
    {
        var mgr = await _f.ManagerAsync(AdvHelpers.Main);
        var uptownMgr = await _f.ManagerAsync(AdvHelpers.Uptown);
        var beans = await Phase2Helpers.IngredientIdAsync(mgr, "Espresso Beans");
        var uptownBefore = (await Phase2Helpers.StockAsync(uptownMgr))["Espresso Beans"];
        var mainBefore = (await Phase2Helpers.StockAsync(mgr))["Espresso Beans"];

        var r1 = await mgr.PostAsJsonAsync($"/api/v1/restocks?locationId={AdvHelpers.Uptown}", new { ingredientId = beans, quantity = 7, locationId = AdvHelpers.Uptown });
        Assert.True(r1.StatusCode is HttpStatusCode.Created or HttpStatusCode.Forbidden, "got " + r1.StatusCode);
        var a1 = await mgr.PostAsJsonAsync($"/api/v1/ingredients/{beans}/adjustments?locationId={AdvHelpers.Uptown}", new { quantityDelta = -3, note = "x", locationId = AdvHelpers.Uptown });
        Assert.True(a1.StatusCode is HttpStatusCode.Created or HttpStatusCode.Forbidden, "got " + a1.StatusCode);

        Assert.Equal(uptownBefore, (await Phase2Helpers.StockAsync(uptownMgr))["Espresso Beans"]);
        var expectedMain = mainBefore + (r1.IsSuccessStatusCode ? 7 : 0) - (a1.IsSuccessStatusCode ? 3 : 0);
        Assert.Equal(expectedMain, (await Phase2Helpers.StockAsync(mgr))["Espresso Beans"]);
        if (r1.IsSuccessStatusCode) Assert.Equal(AdvHelpers.Main, (await r1.ReadAsync<StockMovementDto>()).LocationId);
    }

    [Fact]
    public async Task Owner_at_main_writing_stock_with_another_locationId_still_writes_only_to_the_active_location()
    {
        var owner = await _f.AdminAsync();
        var uptownOwner = await _f.LoginAsync(DevDataSeeder.AdminEmail, DevDataSeeder.AdminPassword, AdvHelpers.Uptown);
        var beans = await Phase2Helpers.IngredientIdAsync(owner, "Espresso Beans");
        var uptownBefore = (await Phase2Helpers.StockAsync(uptownOwner))["Espresso Beans"];
        var res = await owner.PostAsJsonAsync($"/api/v1/restocks?locationId={AdvHelpers.Uptown}", new { ingredientId = beans, quantity = 5, locationId = AdvHelpers.Uptown });
        Assert.True(res.IsSuccessStatusCode);
        Assert.Equal(AdvHelpers.Main, (await res.ReadAsync<StockMovementDto>()).LocationId);
        Assert.Equal(uptownBefore, (await Phase2Helpers.StockAsync(uptownOwner))["Espresso Beans"]);
    }
}

[Collection("api")]
public class StockIsolationAdversarialTests
{
    private readonly ApiFactory _f;
    public StockIsolationAdversarialTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Restock_adjustment_and_waste_at_a_new_location_leave_main_stock_and_ledger_unchanged()
    {
        var (otherId, email, pw) = await _f.CreateOtherLocationAsync(Roles.Manager);
        var other = await _f.LoginAsync(email, pw);
        var mainMgr = await _f.ManagerAsync(AdvHelpers.Main);
        var beans = await Phase2Helpers.IngredientIdAsync(mainMgr, "Espresso Beans");
        var mainBefore = (await Phase2Helpers.StockAsync(mainMgr))["Espresso Beans"];
        var mainLedger = (await mainMgr.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans}/movements?pageSize=100", ApiFactory.Json))!.TotalCount;

        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/v1/restocks", new { ingredientId = beans, quantity = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync($"/api/v1/ingredients/{beans}/adjustments", new { quantityDelta = -4, reason = "Waste", note = "spill" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync($"/api/v1/ingredients/{beans}/adjustments", new { quantityDelta = 2, reason = "Adjustment", note = "count" })).StatusCode);

        Assert.Equal(108m, (await Phase2Helpers.StockAsync(other))["Espresso Beans"]); // seeded 100 + 10 - 4 + 2
        Assert.Equal(mainBefore, (await Phase2Helpers.StockAsync(mainMgr))["Espresso Beans"]);
        var mainLedgerAfter = (await mainMgr.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans}/movements?pageSize=100", ApiFactory.Json))!.TotalCount;
        Assert.Equal(mainLedger, mainLedgerAfter);
        var otherLedger = await other.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans}/movements?pageSize=100", ApiFactory.Json);
        Assert.All(otherLedger!.Data, m => Assert.Equal(otherId, m.LocationId));
    }

    [Fact]
    public async Task Adjustment_cannot_take_stock_below_zero_and_failure_leaves_no_ledger_row()
    {
        var (_, email, pw) = await _f.CreateOtherLocationAsync(Roles.Manager);
        var other = await _f.LoginAsync(email, pw);
        var beans = await Phase2Helpers.IngredientIdAsync(await _f.AdminAsync(), "Espresso Beans");
        var before = (await other.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans}/movements", ApiFactory.Json))!.TotalCount;
        var res = await other.PostAsJsonAsync($"/api/v1/ingredients/{beans}/adjustments", new { quantityDelta = -100000, reason = "Adjustment", note = "too much" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(100m, (await Phase2Helpers.StockAsync(other))["Espresso Beans"]);
        Assert.Equal(before, (await other.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans}/movements", ApiFactory.Json))!.TotalCount);
    }

    [Fact]
    public async Task Sale_with_insufficient_stock_at_one_location_does_not_touch_the_other_and_order_stays_open()
    {
        var (_, email, pw) = await _f.CreateOtherLocationAsync(Roles.Manager);
        var other = await _f.LoginAsync(email, pw);
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner); // 1 Paper Cups per unit
        var cups = await Phase2Helpers.IngredientIdAsync(owner, "Paper Cups");
        // Drain the new location's cups to 0.
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync($"/api/v1/ingredients/{cups}/adjustments", new { quantityDelta = -100, reason = "Waste", note = "drain" })).StatusCode);
        var mainBefore = (await Phase2Helpers.StockAsync(owner))["Paper Cups"];

        var order = await Phase2Helpers.OrderAsync(other, vid);
        var res = await Phase2Helpers.PayAsync(other, order.Id);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Open", (await other.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json))!.Status);
        Assert.Equal(mainBefore, (await Phase2Helpers.StockAsync(owner))["Paper Cups"]);
    }

    [Fact]
    public async Task Track_inventory_override_false_writes_no_sale_ledger_rows_while_true_or_null_still_consume()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner);
        var cups = await Phase2Helpers.IngredientIdAsync(owner, "Paper Cups");
        var main = await _f.CashierAsync();

        async Task<decimal> Cups() => (await Phase2Helpers.StockAsync(main))["Paper Cups"];

        // price-only override: track is null => tracked
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 50 });
        var b = await Cups();
        var o1 = await Phase2Helpers.OrderAsync(main, vid, 2);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(main, o1.Id)).StatusCode);
        Assert.Equal(b - 2, await Cups());

        // explicit true => tracked
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { trackInventoryOverride = true });
        b = await Cups();
        var o2 = await Phase2Helpers.OrderAsync(main, vid);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(main, o2.Id)).StatusCode);
        Assert.Equal(b - 1, await Cups());

        // false => untracked, and no ledger rows for that order
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { trackInventoryOverride = false });
        b = await Cups();
        var o3 = await Phase2Helpers.OrderAsync(main, vid, 3);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(main, o3.Id)).StatusCode);
        Assert.Equal(b, await Cups());
        using var db = _f.NewDb(AdvHelpers.Main);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.SourceDocumentRef == $"order:{o3.Id}"));
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.SourceDocumentRef == $"order:{o2.Id}" && m.IngredientId == cups));
    }

    [Fact]
    public async Task Untracked_variation_sells_even_when_stock_is_zero_at_that_location()
    {
        var (otherId, email, pw) = await _f.CreateOtherLocationAsync(Roles.Manager);
        var other = await _f.LoginAsync(email, pw);
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner);
        var cups = await Phase2Helpers.IngredientIdAsync(owner, "Paper Cups");
        await other.PostAsJsonAsync($"/api/v1/ingredients/{cups}/adjustments", new { quantityDelta = -100, reason = "Waste", note = "drain" });
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{otherId}", new { trackInventoryOverride = false })).StatusCode);
        var order = await Phase2Helpers.OrderAsync(other, vid);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(other, order.Id)).StatusCode);
    }
}

[Collection("api")]
public class PricingAdversarialTests
{
    private readonly ApiFactory _f;
    public PricingAdversarialTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Changing_an_override_does_not_alter_existing_lines_but_new_lines_use_the_new_price_and_completion_keeps_totals()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner, withRecipe: false); // global price 100
        var cashier = await _f.CashierAsync();

        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 80 });
        var order = await Phase2Helpers.OrderAsync(cashier, vid);
        Assert.Equal(80m, order.Items.Single().UnitPrice);

        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 60 });
        var reread = await cashier.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json);
        Assert.Equal(80m, reread!.Items.Single().UnitPrice);
        Assert.Equal(80m, reread.Total);

        var added = await (await cashier.PostAsJsonAsync($"/api/v1/orders/{order.Id}/items", new { itemVariationId = vid, quantity = 1 })).ReadAsync<OrderDto>();
        Assert.Equal(new[] { 60m, 80m }, added.Items.Select(i => i.UnitPrice).OrderBy(x => x).ToArray());
        Assert.Equal(140m, added.Total);

        // Removing the override: old lines still keep their price, completion charges the stored total.
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { });
        var done = await (await Phase2Helpers.PayAsync(cashier, order.Id)).Content.ReadFromJsonAsync<OrderDto>(ApiFactory.Json);
        Assert.Equal(140m, done!.Total);
    }

    [Fact]
    public async Task Override_at_one_location_is_invisible_to_another_locations_manager_and_catalog()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner, withRecipe: false);
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Uptown}", new { priceOverride = 77 });

        var mainMgr = await _f.ManagerAsync(AdvHelpers.Main);
        var list = await mainMgr.GetFromJsonAsync<List<LocationOverrideDto>>($"/api/v1/variations/{vid}/location-overrides", ApiFactory.Json);
        Assert.Empty(list!);

        var uptownMgr = await _f.ManagerAsync(AdvHelpers.Uptown);
        var ul = await uptownMgr.GetFromJsonAsync<List<LocationOverrideDto>>($"/api/v1/variations/{vid}/location-overrides", ApiFactory.Json);
        Assert.Equal(77m, ul!.Single().PriceOverride);

        var mainCashier = await _f.CashierAsync();
        var items = await mainCashier.GetFromJsonAsync<PagedResult<ItemDto>>("/api/v1/items?pageSize=100", ApiFactory.Json);
        Assert.Equal(100m, items!.Data.SelectMany(i => i.Variations).Single(v => v.Id == vid).EffectivePrice);
        Assert.Equal(100m, (await Phase2Helpers.OrderAsync(mainCashier, vid)).Total);
    }

    [Fact]
    public async Task Override_endpoint_validation_and_not_found_cases()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner, withRecipe: false);
        var url = $"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}";

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(url, new { priceOverride = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(url, new { lowStockThresholdOverride = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(url, new { priceOverride = 2_000_000 })).StatusCode);
        // Nothing was stored by the rejected calls.
        Assert.Empty((await owner.GetFromJsonAsync<List<LocationOverrideDto>>($"/api/v1/variations/{vid}/location-overrides", ApiFactory.Json))!);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync($"/api/v1/variations/{Guid.NewGuid()}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Guid.NewGuid()}", new { priceOverride = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/variations/{Guid.NewGuid()}/location-overrides")).StatusCode);

        var mgr = await _f.ManagerAsync(AdvHelpers.Main);
        Assert.Equal(HttpStatusCode.NotFound, (await mgr.PutAsJsonAsync($"/api/v1/variations/{Guid.NewGuid()}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Guid.NewGuid()}", new { priceOverride = 5 })).StatusCode);

        // Zero is a legitimate (free) price; clearing a non-existent override is idempotent 204.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(url, new { priceOverride = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Uptown}", new { })).StatusCode);
    }

    [Fact]
    public async Task Cross_location_owner_session_override_read_lists_all_locations_and_cashier_is_denied()
    {
        var owner = await _f.OwnerAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner, withRecipe: false);
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Main}", new { priceOverride = 10 });
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{AdvHelpers.Uptown}", new { priceOverride = 20 });
        Assert.Equal(2, (await owner.GetFromJsonAsync<List<LocationOverrideDto>>($"/api/v1/variations/{vid}/location-overrides", ApiFactory.Json))!.Count);
        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/variations/{vid}/location-overrides")).StatusCode);
    }
}

[Collection("api")]
public class AuthSessionAdversarialTests
{
    private readonly ApiFactory _f;
    public AuthSessionAdversarialTests(ApiFactory f) => _f = f;

    private async Task<AuthResponse> LoginRaw(HttpClient c, string email, string pw, Guid? loc)
    {
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password = pw, locationId = loc });
        res.EnsureSuccessStatusCode();
        var auth = await res.ReadAsync<AuthResponse>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    [Fact]
    public async Task Tokens_signed_with_a_wrong_key_or_with_edited_claims_are_rejected()
    {
        var real = (await (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword })).ReadAsync<AuthResponse>()).AccessToken;
        var wrongKey = "a-completely-different-signing-key-0123456789abcdef";

        // location_id swapped to Uptown, signed with the wrong key
        var forgedLoc = AdvHelpers.Resign(real, wrongKey, cs => cs.Select(c => c.Type == "location_id" ? new System.Security.Claims.Claim("location_id", AdvHelpers.Uptown.ToString()) : c));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdvHelpers.WithToken(_f, forgedLoc).GetAsync("/api/v1/orders")).StatusCode);

        // role escalated to Owner, wrong key
        var forgedRole = AdvHelpers.Resign(real, wrongKey, cs => cs.Select(c => c.Type == "role" ? new System.Security.Claims.Claim("role", "Owner") : c));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdvHelpers.WithToken(_f, forgedRole).GetAsync("/api/v1/locations")).StatusCode);

        // Payload edited but original signature kept
        var parts = real.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace(AdvHelpers.Main.ToString(), AdvHelpers.Uptown.ToString());
        var spliced = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdvHelpers.WithToken(_f, spliced).GetAsync("/api/v1/orders")).StatusCode);

        // alg=none
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var none = $"{header}.{parts[1]}.";
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdvHelpers.WithToken(_f, none).GetAsync("/api/v1/orders")).StatusCode);

        // Sanity: the real token works.
        Assert.Equal(HttpStatusCode.OK, (await AdvHelpers.WithToken(_f, real).GetAsync("/api/v1/orders")).StatusCode);
    }

    [Fact]
    public async Task Switch_location_rejects_unassigned_unknown_and_inactive_locations_and_anonymous_callers()
    {
        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = AdvHelpers.Main })).StatusCode);

        var owner = await _f.OwnerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = Guid.NewGuid() })).StatusCode);

        var created = await (await owner.PostAsJsonAsync("/api/v1/locations", new { name = "Closing " + Guid.NewGuid().ToString("N")[..5], timeZone = "Asia/Manila", vatRatePercent = 12 })).ReadAsync<LocationDto>();
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/locations/{created.Id}", new { name = created.Name, timeZone = "Asia/Manila", vatRatePercent = 12, active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = created.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.AdminEmail, password = DevDataSeeder.AdminPassword, locationId = created.Id })).StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_log_in_at_an_unassigned_location_and_owner_cannot_be_forged_via_login_body()
    {
        var (otherId, _, _) = await _f.CreateOtherLocationAsync();
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.ManagerEmail, password = DevDataSeeder.ManagerPassword, locationId = otherId });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var res2 = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword, locationId = AdvHelpers.Uptown, isOwner = true, role = "Owner" });
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task Old_refresh_cookie_is_revoked_after_switch_location_and_replay_is_unauthorized()
    {
        var client = _f.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.ManagerEmail, password = DevDataSeeder.ManagerPassword, locationId = AdvHelpers.Main });
        var oldCookie = AdvHelpers.RefreshCookieValue(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.ReadAsync<AuthResponse>()).AccessToken);
        await Phase2Helpers.SwitchAsync(client, AdvHelpers.Uptown);

        var attacker = _f.CreateClient(); // separate cookie jar: replay the stolen old cookie
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Refresh_preserves_owner_cross_location_session_and_a_tampered_cookie_is_rejected()
    {
        var client = _f.CreateClient();
        await LoginRaw(client, DevDataSeeder.AdminEmail, DevDataSeeder.AdminPassword, null);
        var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var auth = await refreshed.ReadAsync<AuthResponse>();
        Assert.Null(auth.User.Location);
        Assert.DoesNotContain(Phase2Helpers.Jwt(auth.AccessToken).Claims, c => c.Type == "location_id");

        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        bad.Headers.Add("Cookie", "cafepos_refresh=bm90LWEtcmVhbC10b2tlbg==");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().SendAsync(bad)).StatusCode);
    }

    [Fact]
    public async Task Removed_assignment_blocks_refresh_and_switch_for_that_location()
    {
        var owner = await _f.OwnerAsync();
        var uid = await AdvHelpers.CreateUserAsync(owner, "Manager", AdvHelpers.Main, AdvHelpers.Uptown);
        var email = AdvHelpers.Emails[uid];

        var client = _f.CreateClient();
        await LoginRaw(client, email, AdvHelpers.Password, AdvHelpers.Uptown);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{uid}/locations/{AdvHelpers.Uptown}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

        var c2 = _f.CreateClient();
        await LoginRaw(c2, email, AdvHelpers.Password, AdvHelpers.Main);
        Assert.Equal(HttpStatusCode.Forbidden, (await c2.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = AdvHelpers.Uptown })).StatusCode);
        // Login at the removed location is refused too.
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = AdvHelpers.Password, locationId = AdvHelpers.Uptown })).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_refresh_switch_or_log_in()
    {
        var owner = await _f.OwnerAsync();
        var uid = await AdvHelpers.CreateUserAsync(owner, "Manager", AdvHelpers.Main, AdvHelpers.Uptown);
        var email = AdvHelpers.Emails[uid];
        var client = _f.CreateClient();
        await LoginRaw(client, email, AdvHelpers.Password, AdvHelpers.Main);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{uid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = AdvHelpers.Uptown })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = AdvHelpers.Password, locationId = AdvHelpers.Main })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact(Skip = "BUG?: access tokens are not re-validated against StaffAssignments/User.Active, so a deactivated user or a removed assignment keeps full API access at that location until the 30 min JWT expires. Docs only promise refresh/switch/me are blocked.")]
    public async Task Deactivated_users_existing_access_token_cannot_act_on_location_data()
    {
        var owner = await _f.OwnerAsync();
        var uid = await AdvHelpers.CreateUserAsync(owner, "Manager", AdvHelpers.Main);
        var client = _f.CreateClient();
        await LoginRaw(client, AdvHelpers.Emails[uid], AdvHelpers.Password, null);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{uid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/orders")).StatusCode);
    }

    [Fact(Skip = "BUG?: removed assignment does not invalidate the already-issued access token (role + location_id claims stay valid up to 30 min).")]
    public async Task Removed_assignments_existing_access_token_cannot_act_at_that_location()
    {
        var owner = await _f.OwnerAsync();
        var uid = await AdvHelpers.CreateUserAsync(owner, "Manager", AdvHelpers.Main, AdvHelpers.Uptown);
        var client = _f.CreateClient();
        await LoginRaw(client, AdvHelpers.Emails[uid], AdvHelpers.Password, AdvHelpers.Uptown);
        await owner.DeleteAsync($"/api/v1/users/{uid}/locations/{AdvHelpers.Uptown}");
        Assert.True((await client.GetAsync("/api/v1/orders")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
    }
}

[Collection("api")]
public class AdminBoundaryAdversarialTests
{
    private readonly ApiFactory _f;
    public AdminBoundaryAdversarialTests(ApiFactory f) => _f = f;

    private async Task<(Guid locId, HttpClient admin, Guid adminId)> NewAdminAsync()
    {
        var (locId, email, pw) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(email, pw);
        var me = await admin.GetFromJsonAsync<UserInfoDto>("/api/v1/auth/me", ApiFactory.Json);
        return (locId, admin, me!.Id);
    }

    private async Task<Guid> MainCashierIdAsync()
    {
        var owner = await _f.OwnerAsync();
        var page = await owner.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100&search=" + DevDataSeeder.CashierEmail, ApiFactory.Json);
        return page!.Data.Single(u => u.Email == DevDataSeeder.CashierEmail).Id;
    }

    [Fact]
    public async Task Admin_cannot_read_edit_deactivate_or_unassign_users_of_another_location()
    {
        var (_, admin, _) = await NewAdminAsync();
        var victim = await MainCashierIdAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{victim}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/users/{victim}", new { displayName = "Pwned", active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/users/{victim}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/v1/users/{victim}/locations/{AdvHelpers.Main}")).StatusCode);

        // Victim is untouched and can still log in.
        Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword })).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_see_edit_or_deactivate_an_owner_or_use_other_location_filters()
    {
        var (locId, admin, _) = await NewAdminAsync();
        var ownerId = await AdvHelpers.OwnerIdAsync(await _f.OwnerAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/users/{ownerId}", new { displayName = "x", active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/users/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/users/{ownerId}/locations", new { locationId = locId, role = "Cashier" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/users?locationId={AdvHelpers.Main}")).StatusCode);
        var own = await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100", ApiFactory.Json);
        Assert.All(own!.Data, u => { Assert.False(u.IsOwner); Assert.Contains(u.Locations, l => l.LocationId == locId); });
    }

    [Fact]
    public async Task Admin_cannot_grant_owner_create_owner_or_assign_outside_own_location()
    {
        var (locId, admin, adminId) = await NewAdminAsync();
        string E() => $"adm-{Guid.NewGuid():N}@cafe.local";

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/users", new { email = E(), displayName = "O", password = "AdvUser#12345", isOwner = true, assignments = new[] { new { locationId = locId, role = "Cashier" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/users", new { email = E(), displayName = "M", password = "AdvUser#12345", assignments = new[] { new { locationId = AdvHelpers.Main, role = "Cashier" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/users", new { email = E(), displayName = "Both", password = "AdvUser#12345", assignments = new[] { new { locationId = locId, role = "Cashier" }, new { locationId = AdvHelpers.Main, role = "Cashier" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/users", new { email = E(), displayName = "None", password = "AdvUser#12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/users", new { email = E(), displayName = "R", password = "AdvUser#12345", assignments = new[] { new { locationId = locId, role = "Owner" } } })).StatusCode);

        // Assigning an existing user to another location, or granting Owner via update (including to self), is refused.
        var cashierId = await MainCashierIdAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/users/{cashierId}/locations", new { locationId = AdvHelpers.Main, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/users/{adminId}", new { displayName = "Me", isOwner = true })).StatusCode);
        var asOwner = await _f.OwnerAsync();
        var self = await asOwner.GetFromJsonAsync<UserDto>($"/api/v1/users/{adminId}", ApiFactory.Json);
        Assert.False(self!.IsOwner);
    }

    [Fact]
    public async Task Admin_cannot_demote_deactivate_or_unassign_self_and_cannot_edit_foreign_locations()
    {
        var (locId, admin, adminId) = await NewAdminAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"/api/v1/users/{adminId}", new { displayName = "Me", active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.DeleteAsync($"/api/v1/users/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.DeleteAsync($"/api/v1/users/{adminId}/locations/{locId}")).StatusCode);

        var body = new { name = "Hax", timeZone = "Asia/Manila", vatRatePercent = 0 };
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/locations/{AdvHelpers.Main}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/locations/{AdvHelpers.Main}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/locations", new { name = "New", timeZone = "Asia/Manila", vatRatePercent = 12 })).StatusCode);
        // Owner-only fields, even when no-op-ish
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/locations/{locId}", new { name = "n", timeZone = "Asia/Manila", vatRatePercent = 12, stripeTerminalLocationId = "" })).StatusCode);

        var main = await (await _f.OwnerAsync()).GetFromJsonAsync<List<LocationDto>>("/api/v1/locations", ApiFactory.Json);
        Assert.NotEqual("Hax", main!.Single(l => l.Id == AdvHelpers.Main).Name);
    }

    [Fact]
    public async Task Admin_can_manage_staff_at_own_location_only()
    {
        var (locId, admin, _) = await NewAdminAsync();
        var email = $"adm-{Guid.NewGuid():N}@cafe.local";
        var created = await admin.PostAsJsonAsync("/api/v1/users", new { email, displayName = "Hire", password = "AdvUser#12345", assignments = new[] { new { locationId = locId, role = "Cashier" } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var uid = (await created.ReadAsync<UserDto>()).Id;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/users/{uid}/locations", new { locationId = locId, role = "Manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/users/{uid}", new { displayName = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{uid}")).StatusCode);
    }

    [Fact]
    public async Task Cashier_and_manager_get_403_on_all_admin_endpoints_and_owner_can_do_all()
    {
        var someUser = await MainCashierIdAsync();
        var body = new { name = "x", timeZone = "Asia/Manila", vatRatePercent = 12 };
        var newUser = new { email = $"x-{Guid.NewGuid():N}@cafe.local", displayName = "x", password = "AdvUser#12345", assignments = new[] { new { locationId = AdvHelpers.Main, role = "Cashier" } } };

        foreach (var c in new[] { await _f.CashierAsync(), await _f.ManagerAsync(AdvHelpers.Main) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/v1/users/{someUser}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/users", newUser)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/v1/users/{someUser}", new { displayName = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/api/v1/users/{someUser}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"/api/v1/users/{someUser}/locations", new { locationId = AdvHelpers.Main, role = "Cashier" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/api/v1/users/{someUser}/locations/{AdvHelpers.Main}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/locations", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/v1/locations/{AdvHelpers.Main}", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/v1/locations/{AdvHelpers.Main}/staff")).StatusCode);
        }
        // Unauthenticated
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/v1/users")).StatusCode);

        var owner = await _f.OwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/users/{someUser}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/locations/{AdvHelpers.Uptown}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/locations/{AdvHelpers.Uptown}", new { name = "Brew & Bean - Uptown Branch", timeZone = "Asia/Manila", vatRatePercent = 12 })).StatusCode);
        var promote = await owner.PostAsJsonAsync("/api/v1/users", new { email = $"o-{Guid.NewGuid():N}@cafe.local", displayName = "Co-owner", password = "AdvUser#12345", isOwner = true });
        Assert.Equal(HttpStatusCode.Created, promote.StatusCode);
        Assert.True((await promote.ReadAsync<UserDto>()).IsOwner);
    }
}
