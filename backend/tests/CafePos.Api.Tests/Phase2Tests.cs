using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CafePos.Api.Tests;

internal static class Phase2Helpers
{
    public static readonly Guid Main = DevDataSeeder.MainLocationId;
    public static readonly Guid Uptown = DevDataSeeder.UptownLocationId;

    public static JwtSecurityToken Jwt(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    public static async Task<AuthResponse> SwitchAsync(HttpClient c, Guid? locationId)
    {
        var res = await c.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId });
        res.EnsureSuccessStatusCode();
        var auth = await res.ReadAsync<AuthResponse>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    public static async Task<Dictionary<string, decimal>> StockAsync(HttpClient c, string query = "") =>
        (await c.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients" + query, ApiFactory.Json))!.ToDictionary(i => i.Name, i => i.QuantityOnHand!.Value);

    public static async Task<Guid> IngredientIdAsync(HttpClient c, string name) =>
        (await c.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients?locationId={Main}", ApiFactory.Json))!.First(i => i.Name == name).Id;

    public static async Task<OrderDto> OrderAsync(HttpClient c, Guid variationId, int qty = 1)
    {
        var res = await c.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { new { itemVariationId = variationId, quantity = qty } } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.ReadAsync<OrderDto>();
    }

    public static Task<HttpResponseMessage> PayAsync(HttpClient c, Guid orderId) =>
        c.PostWithKeyAsync($"/api/v1/orders/{orderId}/complete", new { paymentMethod = "cash", amountTendered = 10000 }, Guid.NewGuid().ToString());

    /// <summary>Creates a fresh sellable item (price 100, recipe: 1 Paper Cups) so override tests never disturb shared seed data.</summary>
    public static async Task<Guid> NewVariationAsync(ApiFactory f, HttpClient owner, bool withRecipe = true)
    {
        var cats = await owner.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", ApiFactory.Json);
        var item = await (await owner.PostAsJsonAsync("/api/v1/items", new
        {
            categoryId = cats![0].Id, name = "P2 " + Guid.NewGuid().ToString("N")[..6],
            variations = new[] { new { name = "One", sku = "P2-" + Guid.NewGuid().ToString("N")[..8], price = 100 } }
        })).ReadAsync<ItemDto>();
        var vid = item.Variations.Single().Id;
        if (withRecipe)
        {
            var ingredients = await owner.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json);
            var cups = ingredients!.First(i => i.Name == "Paper Cups").Id;
            (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/recipe", new { lines = new[] { new { ingredientId = cups, quantity = 1 } } })).EnsureSuccessStatusCode();
        }
        return vid;
    }
}

[Collection("api")]
public class LoginAndSwitchLocationTests
{
    private readonly ApiFactory _f;
    public LoginAndSwitchLocationTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Single_assignment_user_gets_that_location_and_the_jwt_carries_location_and_role()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });
        var auth = await res.ReadAsync<AuthResponse>();
        var jwt = Phase2Helpers.Jwt(auth.AccessToken);
        Assert.Equal(Phase2Helpers.Main.ToString(), jwt.Claims.Single(c => c.Type == "location_id").Value);
        Assert.Equal("Cashier", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Single(auth.User.Locations);
    }

    [Fact]
    public async Task Multi_location_user_must_choose_then_can_switch_and_each_location_has_independent_stock()
    {
        var anon = _f.CreateClient();
        var ambiguous = await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.ManagerEmail, password = DevDataSeeder.ManagerPassword });
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Contains("locations", await ambiguous.Content.ReadAsStringAsync());

        var c = await _f.ManagerAsync(Phase2Helpers.Main);
        var mainTea = (await Phase2Helpers.StockAsync(c))["Tea Bags"];
        var me = await c.GetFromJsonAsync<UserInfoDto>("/api/v1/auth/me", ApiFactory.Json);
        Assert.Equal(2, me!.Locations.Count);
        Assert.Equal(Phase2Helpers.Main, me.Location!.Id);

        var auth = await Phase2Helpers.SwitchAsync(c, Phase2Helpers.Uptown);
        Assert.Equal(Phase2Helpers.Uptown, auth.User.Location!.Id);
        Assert.Equal(Phase2Helpers.Uptown.ToString(), Phase2Helpers.Jwt(auth.AccessToken).Claims.Single(x => x.Type == "location_id").Value);
        var uptownTea = (await Phase2Helpers.StockAsync(c))["Tea Bags"];
        Assert.NotEqual(mainTea, uptownTea);
    }

    [Fact]
    public async Task Switching_to_an_unassigned_location_is_403_and_non_owners_cannot_go_cross_location()
    {
        var cashier = await _f.CashierAsync(); // Main only
        var denied = await cashier.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = Phase2Helpers.Uptown });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var none = await cashier.PostAsJsonAsync("/api/v1/auth/switch-location", new { locationId = (Guid?)null });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task Refresh_keeps_the_location_chosen_by_switch_location()
    {
        var client = _f.CreateClient(); // keeps the refresh cookie
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.ManagerEmail, password = DevDataSeeder.ManagerPassword, locationId = Phase2Helpers.Main });
        var auth = await login.ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await Phase2Helpers.SwitchAsync(client, Phase2Helpers.Uptown);

        var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(Phase2Helpers.Uptown, (await refreshed.ReadAsync<AuthResponse>()).User.Location!.Id);
    }

    [Fact]
    public async Task Owner_login_without_location_is_cross_location_and_can_select_any_location()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.AdminEmail, password = DevDataSeeder.AdminPassword });
        var auth = await res.ReadAsync<AuthResponse>();
        Assert.Equal("Owner", auth.User.Role);
        Assert.True(auth.User.IsOwner);
        Assert.Null(auth.User.Location);
        Assert.DoesNotContain(Phase2Helpers.Jwt(auth.AccessToken).Claims, c => c.Type == "location_id");
        Assert.True(auth.User.Locations.Count >= 2);

        var c = await _f.OwnerAsync();
        Assert.Equal(Phase2Helpers.Uptown, (await Phase2Helpers.SwitchAsync(c, Phase2Helpers.Uptown)).User.Location!.Id);
        Assert.Null((await Phase2Helpers.SwitchAsync(c, null)).User.Location);

        // A location-scoped write without an active location is a 400, not a silent cross-location write.
        var nolocation = await _f.OwnerAsync();
        var res2 = await nolocation.PostAsJsonAsync("/api/v1/restocks", new { ingredientId = (await Phase2Helpers.IngredientIdAsync(nolocation, "Paper Cups")), quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }
}

[Collection("api")]
public class LocationIsolationApiTests
{
    private readonly ApiFactory _f;
    public LocationIsolationApiTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Sale_at_one_location_does_not_change_the_other_locations_stock_or_ledger()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var cro = await _f.VariationIdAsync(main, "CRO-1");
        var uptownBefore = (await Phase2Helpers.StockAsync(uptown))["Butter Croissant"];
        var mainBefore = (await Phase2Helpers.StockAsync(main))["Butter Croissant"];

        var order = await Phase2Helpers.OrderAsync(main, cro, 2);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(main, order.Id)).StatusCode);

        Assert.Equal(mainBefore - 2, (await Phase2Helpers.StockAsync(main))["Butter Croissant"]);
        Assert.Equal(uptownBefore, (await Phase2Helpers.StockAsync(uptown))["Butter Croissant"]);

        using var db = _f.NewDb(Phase2Helpers.Uptown);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.SourceDocumentRef == $"order:{order.Id}"));
    }

    [Fact]
    public async Task Cashier_cannot_read_another_locations_order_or_use_the_locationId_filter()
    {
        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var order = await Phase2Helpers.OrderAsync(main, await _f.VariationIdAsync(main, "CRO-1"));

        Assert.Equal(HttpStatusCode.NotFound, (await uptown.GetAsync($"/api/v1/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await uptown.GetAsync($"/api/v1/orders?locationId={Phase2Helpers.Main}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await uptown.GetAsync($"/api/v1/ingredients/low-stock?locationId={Phase2Helpers.Main}")).StatusCode);
        // Sending your own location is fine (it is not trusted for anything else).
        Assert.Equal(HttpStatusCode.OK, (await uptown.GetAsync($"/api/v1/orders?locationId={Phase2Helpers.Uptown}")).StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_pass_another_assigned_locations_id_without_switching()
    {
        var mgr = await _f.ManagerAsync(Phase2Helpers.Main);
        var res = await mgr.GetAsync($"/api/v1/ingredients/low-stock?locationId={Phase2Helpers.Uptown}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Owner_cross_location_views_orders_stock_and_movements_of_any_location()
    {
        var uptownCashier = await _f.UptownCashierAsync();
        var mainCashier = await _f.CashierAsync();
        var uptownOrder = await Phase2Helpers.OrderAsync(uptownCashier, await _f.VariationIdAsync(uptownCashier, "CRO-1"));
        var mainOrder = await Phase2Helpers.OrderAsync(mainCashier, await _f.VariationIdAsync(mainCashier, "CRO-1"));
        await Phase2Helpers.PayAsync(uptownCashier, uptownOrder.Id);

        var owner = await _f.OwnerAsync(); // no active location

        var all = await owner.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders?pageSize=100", ApiFactory.Json);
        Assert.Contains(all!.Data, o => o.Id == uptownOrder.Id);
        Assert.Contains(all.Data, o => o.Id == mainOrder.Id);

        var onlyUptown = await owner.GetFromJsonAsync<PagedResult<OrderDto>>($"/api/v1/orders?pageSize=100&locationId={Phase2Helpers.Uptown}", ApiFactory.Json);
        Assert.All(onlyUptown!.Data, o => Assert.Equal(Phase2Helpers.Uptown, o.LocationId));
        Assert.Contains(onlyUptown.Data, o => o.Id == uptownOrder.Id);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/orders/{mainOrder.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/orders/{uptownOrder.Id}/receipt")).StatusCode);
        // ...but mutation needs an active location.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/orders/{mainOrder.Id}/cancel", null)).StatusCode);

        var uptownLow = await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients/low-stock?locationId={Phase2Helpers.Uptown}", ApiFactory.Json);
        Assert.DoesNotContain(uptownLow!, i => i.Name == "Tea Bags"); // Tea Bags is low at Main only
        var mainLow = await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients/low-stock?locationId={Phase2Helpers.Main}", ApiFactory.Json);
        Assert.Contains(mainLow!, i => i.Name == "Tea Bags");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/ingredients/low-stock")).StatusCode); // no location, no filter

        var beans = (await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients?locationId={Phase2Helpers.Uptown}", ApiFactory.Json))!.First(i => i.Name == "Espresso Beans");
        var moves = await owner.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{beans.Id}/movements?locationId={Phase2Helpers.Uptown}", ApiFactory.Json);
        Assert.NotEmpty(moves!.Data);
        Assert.All(moves.Data, m => Assert.Equal(Phase2Helpers.Uptown, m.LocationId));
    }

    [Fact]
    public async Task Staff_assignments_are_filtered_by_location_at_the_data_layer()
    {
        using var main = _f.NewDb(Phase2Helpers.Main);
        Assert.NotEmpty(await main.StaffAssignments.ToListAsync());
        Assert.All(await main.StaffAssignments.ToListAsync(), a => Assert.Equal(Phase2Helpers.Main, a.LocationId));
        using var none = _f.NewDb(null);
        Assert.Empty(await none.StaffAssignments.ToListAsync());
        Assert.NotEmpty(await none.StaffAssignments.IgnoreQueryFilters().ToListAsync());
    }
}

[Collection("api")]
public class LocationOverrideTests
{
    private readonly ApiFactory _f;
    public LocationOverrideTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Override_price_applies_only_at_its_location_and_is_cleared_with_all_nulls()
    {
        var owner = await _f.AdminAsync(); // Owner at Main
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner);

        var put = await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Main}", new { priceOverride = 80 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = await put.ReadAsync<LocationOverrideDto>();
        Assert.Equal(80m, dto.PriceOverride);
        Assert.Equal(80m, dto.EffectivePrice);

        var cashier = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        Assert.Equal(80m, (await Phase2Helpers.OrderAsync(cashier, vid)).Total);
        Assert.Equal(100m, (await Phase2Helpers.OrderAsync(uptown, vid)).Total);

        var variations = await cashier.GetFromJsonAsync<List<VariationDto>>($"/api/v1/items/{(await cashier.GetFromJsonAsync<PagedResult<ItemDto>>("/api/v1/items?pageSize=100", ApiFactory.Json))!.Data.First(i => i.Variations.Any(v => v.Id == vid)).Id}/variations", ApiFactory.Json);
        Assert.Equal(80m, variations!.Single().EffectivePrice);
        Assert.Equal(100m, variations!.Single().Price);

        var all = await owner.GetFromJsonAsync<List<LocationOverrideDto>>($"/api/v1/variations/{vid}/location-overrides", ApiFactory.Json);
        Assert.Single(all!);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Main}", new { })).StatusCode);
        Assert.Equal(100m, (await Phase2Helpers.OrderAsync(cashier, vid)).Total);
    }

    [Fact]
    public async Task Override_writes_need_manager_role_and_access_to_the_target_location()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner);
        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Main}", new { priceOverride = 1 })).StatusCode);

        var mgr = await _f.ManagerAsync(Phase2Helpers.Main);
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Uptown}", new { priceOverride = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Main}", new { priceOverride = 90 })).StatusCode);
        // Owner can set the other location's override while sitting at Main.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Uptown}", new { priceOverride = 95 })).StatusCode);
    }

    [Fact]
    public async Task Track_inventory_override_false_skips_stock_consumption_at_that_location_only()
    {
        var owner = await _f.AdminAsync();
        var vid = await Phase2Helpers.NewVariationAsync(_f, owner); // consumes 1 Paper Cups
        await owner.PutAsJsonAsync($"/api/v1/variations/{vid}/location-overrides/{Phase2Helpers.Main}", new { trackInventoryOverride = false });

        var main = await _f.CashierAsync();
        var uptown = await _f.UptownCashierAsync();
        var mainBefore = (await Phase2Helpers.StockAsync(main))["Paper Cups"];
        var uptownBefore = (await Phase2Helpers.StockAsync(uptown))["Paper Cups"];

        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(main, (await Phase2Helpers.OrderAsync(main, vid)).Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Phase2Helpers.PayAsync(uptown, (await Phase2Helpers.OrderAsync(uptown, vid)).Id)).StatusCode);

        Assert.Equal(mainBefore, (await Phase2Helpers.StockAsync(main))["Paper Cups"]);
        Assert.Equal(uptownBefore - 1, (await Phase2Helpers.StockAsync(uptown))["Paper Cups"]);
    }
}

[Collection("api")]
public class LocationAndUserManagementTests
{
    private readonly ApiFactory _f;
    public LocationAndUserManagementTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Only_owner_creates_locations_and_lists_all_while_others_list_their_own()
    {
        var owner = await _f.OwnerAsync();
        var created = await owner.PostAsJsonAsync("/api/v1/locations", new { name = "Pop-up " + Guid.NewGuid().ToString("N")[..5], timeZone = "Asia/Manila", vatRatePercent = 12 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var loc = await created.ReadAsync<LocationDto>();
        Assert.Contains((await owner.GetFromJsonAsync<List<LocationDto>>("/api/v1/locations", ApiFactory.Json))!, l => l.Id == loc.Id);

        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/v1/locations", new { name = "x", timeZone = "Asia/Manila" })).StatusCode);
        var mine = await cashier.GetFromJsonAsync<List<LocationDto>>("/api/v1/locations", ApiFactory.Json);
        Assert.Equal(Phase2Helpers.Main, mine!.Single().Id);

        var bad = await owner.PostAsJsonAsync("/api/v1/locations", new { name = "Bad TZ", timeZone = "Mars/Olympus" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);

        var upd = await owner.PutAsJsonAsync($"/api/v1/locations/{loc.Id}", new { name = loc.Name, timeZone = "Asia/Manila", vatRatePercent = 12, stripeTerminalLocationId = "tml_123" });
        Assert.Equal("tml_123", (await upd.ReadAsync<LocationDto>()).StripeTerminalLocationId);
    }

    [Fact]
    public async Task Location_admin_edits_only_own_location_and_cannot_touch_owner_fields()
    {
        var (otherId, email, password) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(email, password);
        var body = new { name = "Renamed", timeZone = "Asia/Manila", vatRatePercent = 10 };
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/locations/{otherId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/locations/{Phase2Helpers.Main}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/locations/{otherId}", new { name = "Renamed", timeZone = "Asia/Manila", vatRatePercent = 10, active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/locations/{otherId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/locations/{Phase2Helpers.Main}/staff")).StatusCode);
    }

    [Fact]
    public async Task Staff_list_shows_assignments_and_cashiers_are_denied()
    {
        var owner = await _f.OwnerAsync();
        var staff = await owner.GetFromJsonAsync<List<StaffMemberDto>>($"/api/v1/locations/{Phase2Helpers.Uptown}/staff", ApiFactory.Json);
        Assert.Contains(staff!, s => s.Email == DevDataSeeder.UptownCashierEmail && s.Role == "Cashier");
        Assert.Contains(staff!, s => s.Email == DevDataSeeder.ManagerEmail && s.Role == "Manager");
        Assert.DoesNotContain(staff!, s => s.Email == DevDataSeeder.CashierEmail);

        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/locations/{Phase2Helpers.Main}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Owner_manages_users_and_assignments_end_to_end()
    {
        var owner = await _f.OwnerAsync();
        var email = $"new-{Guid.NewGuid():N}@cafe.local";
        var create = await owner.PostAsJsonAsync("/api/v1/users", new
        {
            email, displayName = "New Hire", password = "NewHire#12345",
            assignments = new[] { new { locationId = Phase2Helpers.Main, role = "Cashier" } }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var user = await create.ReadAsync<UserDto>();
        Assert.Single(user.Locations);

        // A second assignment makes the next login ambiguous.
        var assigned = await owner.PostAsJsonAsync($"/api/v1/users/{user.Id}/locations", new { locationId = Phase2Helpers.Uptown, role = "Manager" });
        Assert.Equal(2, (await assigned.ReadAsync<UserDto>()).Locations.Count);
        var login = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "NewHire#12345" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{user.Id}/locations/{Phase2Helpers.Uptown}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "NewHire#12345" })).StatusCode);

        var dup = await owner.PostAsJsonAsync("/api/v1/users", new { email, displayName = "Dup", password = "NewHire#12345", assignments = new[] { new { locationId = Phase2Helpers.Main, role = "Cashier" } } });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var weak = await owner.PostAsJsonAsync("/api/v1/users", new { email = $"w-{Guid.NewGuid():N}@cafe.local", displayName = "Weak", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "NewHire#12345" })).StatusCode);
        var page = await owner.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v1/users?locationId={Phase2Helpers.Main}&pageSize=100", ApiFactory.Json);
        Assert.Contains(page!.Data, u => u.Id == user.Id && !u.Active);
    }

    [Fact]
    public async Task Location_admin_is_confined_to_own_location_and_cannot_create_owners()
    {
        var (otherId, email, password) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(email, password);

        var denied = await admin.PostAsJsonAsync("/api/v1/users", new { email = $"o-{Guid.NewGuid():N}@cafe.local", displayName = "Sneaky", password = "Sneaky#12345", isOwner = true, assignments = new[] { new { locationId = otherId, role = "Cashier" } } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var wrongLoc = await admin.PostAsJsonAsync("/api/v1/users", new { email = $"o-{Guid.NewGuid():N}@cafe.local", displayName = "Wrong", password = "Wrong#12345", assignments = new[] { new { locationId = Phase2Helpers.Main, role = "Cashier" } } });
        Assert.Equal(HttpStatusCode.Forbidden, wrongLoc.StatusCode);
        var ok = await admin.PostAsJsonAsync("/api/v1/users", new { email = $"o-{Guid.NewGuid():N}@cafe.local", displayName = "Fine", password = "Fine#12345", assignments = new[] { new { locationId = otherId, role = "Cashier" } } });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var list = await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100", ApiFactory.Json);
        Assert.All(list!.Data, u => Assert.Contains(u.Locations, l => l.LocationId == otherId));
        Assert.DoesNotContain(list.Data, u => u.IsOwner);

        // The seeded Main cashier and the Owner are invisible (404) to this admin.
        using var db = _f.NewDb(null);
        var mainCashier = await db.Users.FirstAsync(u => u.Email == DevDataSeeder.CashierEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{mainCashier.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/users/{mainCashier.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/users?locationId={Phase2Helpers.Main}")).StatusCode);
    }
}

[Collection("api")]
public class MigrationAndSeedTests
{
    private readonly ApiFactory _f;
    public MigrationAndSeedTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Seed_creates_two_locations_with_independent_stock_and_an_owner_without_assignments()
    {
        using var db = _f.NewDb(null);
        Assert.Equal(2, await db.Locations.CountAsync(l => l.Id == Phase2Helpers.Main || l.Id == Phase2Helpers.Uptown));
        var owner = await db.Users.FirstAsync(u => u.Email == DevDataSeeder.AdminEmail);
        Assert.True(owner.IsOwner);
        Assert.False(await db.StaffAssignments.IgnoreQueryFilters().AnyAsync(a => a.UserId == owner.Id));
        var perLocation = await db.IngredientStocks.IgnoreQueryFilters().GroupBy(s => s.LocationId).Select(g => g.Count()).ToListAsync();
        Assert.True(perLocation.Count >= 2);
    }

    /// <summary>Applies InitialCreate (Phase 1), inserts Phase-1-shaped data, then applies Phase2MultiLocation and checks nothing was lost.</summary>
    [Fact]
    public async Task Phase2_migration_preserves_phase1_data_and_promotes_the_sole_admin_to_owner()
    {
        var csb = new NpgsqlConnectionStringBuilder(_f.ConnectionString) { Database = "cafepos_mig_" + Guid.NewGuid().ToString("N") };
        var options = new DbContextOptionsBuilder<CafeDbContext>().UseNpgsql(csb.ConnectionString).Options;
        var locId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        try
        {
            await using (var db = new CafeDbContext(options, null))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261016132257_InitialCreate");
            }
            await using (var conn = new NpgsqlConnection(csb.ConnectionString))
            {
                await conn.OpenAsync();
                async Task Exec(string sql, params (string, object)[] ps)
                {
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
                    await cmd.ExecuteNonQueryAsync();
                }
                await Exec("""INSERT INTO "Locations" ("Id","Name","TimeZone","VatRatePercent","Active","LastOrderNumber") VALUES (@l,'Main','Asia/Manila',12,true,1)""", ("l", locId));
                foreach (var (id, mail) in new[] { (adminId, "a@x.local"), (cashierId, "c@x.local") })
                    await Exec("""INSERT INTO "AspNetUsers" ("Id","DisplayName","Active","Email","UserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES (@i,'U',true,@m,@m,true,false,false,true,0)""", ("i", id), ("m", mail));
                await Exec("""INSERT INTO "StaffAssignments" ("UserId","LocationId","Role") VALUES (@u,@l,'Admin'),(@c,@l,'Cashier')""", ("u", adminId), ("c", cashierId), ("l", locId));
                await Exec("""INSERT INTO "Orders" ("Id","LocationId","OrderNumber","Status","CashierId","CashierName","VatRatePercent","Total","VatAmount","CreatedAt") VALUES (@o,@l,1,'Completed',@c,'C',12,150,16.07,now())""", ("o", orderId), ("l", locId), ("c", cashierId));
            }

            await using (var db = new CafeDbContext(options, null))
            {
                await db.Database.MigrateAsync(); // applies Phase2MultiLocation
            }

            await using var verify = new CafeDbContext(options, new FixedLocation(locId));
            var order = await verify.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal(locId, order.LocationId);
            Assert.Equal(150m, order.Total);
            Assert.Equal(2, await verify.StaffAssignments.CountAsync());
            Assert.True((await verify.Users.SingleAsync(u => u.Id == adminId)).IsOwner);
            Assert.False((await verify.Users.SingleAsync(u => u.Id == cashierId)).IsOwner);
            Assert.Null((await verify.Locations.SingleAsync()).StripeTerminalLocationId);
            Assert.Empty(await verify.ItemVariationLocationOverrides.ToListAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            var admin = new NpgsqlConnectionStringBuilder(csb.ConnectionString) { Database = "postgres", Pooling = false };
            await using var conn = new NpgsqlConnection(admin.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{csb.Database}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
