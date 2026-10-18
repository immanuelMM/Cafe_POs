using System.Net;
using System.Net.Http.Json;
using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;

namespace CafePos.Api.Tests;

/// <summary>Contract changes of the Phase 2 fix batch: Admin scoping, email-based assignment, cross-location ingredients, 400 vs 404.</summary>
[Collection("api")]
public class Phase2FixBatchTests
{
    private readonly ApiFactory _f;
    public Phase2FixBatchTests(ApiFactory f) => _f = f;

    private static string Mail() => $"fx-{Guid.NewGuid():N}@cafe.local";
    private const string Pwd = "FixBatch#12345";

    private static async Task<UserDto> CreateAsync(HttpClient owner, string email, string role, params Guid[] locations)
    {
        var res = await owner.PostAsJsonAsync("/api/v1/users", new
        {
            email, displayName = "Fix " + role, password = Pwd,
            assignments = locations.Select(l => new { locationId = l, role }).ToArray()
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.ReadAsync<UserDto>();
    }

    private async Task<HttpResponseMessage> LoginAsync(string email, Guid? locationId = null) =>
        await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Pwd, locationId });

    [Fact]
    public async Task Owner_cross_location_session_gets_400_not_404_or_500_for_order_mutations()
    {
        var owner = await _f.OwnerAsync();
        var cro = await _f.VariationIdAsync(owner, "CRO-1");
        var line = new { itemVariationId = cro, quantity = 1 };
        var create = await owner.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { line } });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        var main = await _f.CashierAsync();
        var order = await Phase2Helpers.OrderAsync(main, cro);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/orders/{order.Id}/items", line)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/orders/{order.Id}/items/{order.Items[0].Id}", line)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"/api/v1/orders/{order.Id}/items/{order.Items[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/orders/{order.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Phase2Helpers.PayAsync(owner, order.Id)).StatusCode);
        // Reads still work across locations.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task Ingredients_in_an_owner_cross_location_session_are_the_global_catalog_without_stock()
    {
        var owner = await _f.OwnerAsync();
        var catalog = await owner.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json);
        Assert.Contains(catalog!, i => i.Name == "Whole Milk");
        Assert.All(catalog!, i => { Assert.Null(i.QuantityOnHand); Assert.Null(i.IsLowStock); });

        var main = await owner.GetFromJsonAsync<List<IngredientDto>>($"/api/v1/ingredients?locationId={Phase2Helpers.Main}", ApiFactory.Json);
        Assert.All(main!, i => Assert.NotNull(i.QuantityOnHand));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/ingredients/low-stock")).StatusCode);
    }

    [Fact]
    public async Task Non_owner_users_need_at_least_one_assignment()
    {
        var owner = await _f.OwnerAsync();
        var none = await owner.PostAsJsonAsync("/api/v1/users", new { email = Mail(), displayName = "Nobody", password = Pwd });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var asOwner = await owner.PostAsJsonAsync("/api/v1/users", new { email = Mail(), displayName = "Second Owner", password = Pwd, isOwner = true });
        Assert.Equal(HttpStatusCode.Created, asOwner.StatusCode);

        // Demoting an Owner who has no assignments would create a user who can never log in.
        var created = await asOwner.ReadAsync<UserDto>();
        var demote = await owner.PutAsJsonAsync($"/api/v1/users/{created.Id}", new { displayName = "Second Owner", isOwner = false });
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
    }

    [Fact]
    public async Task Admin_deactivate_removes_only_the_assignment_at_own_location_when_user_has_others()
    {
        var owner = await _f.OwnerAsync();
        var (locA, adminMail, adminPwd) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(adminMail, adminPwd);
        var email = Mail();
        var user = await CreateAsync(owner, email, "Cashier", locA, Phase2Helpers.Main);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{user.Id}")).StatusCode);

        var seen = await owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json);
        Assert.True(seen!.Active);                       // not globally deactivated
        Assert.Equal(Phase2Helpers.Main, seen.Locations.Single().LocationId); // only the Main assignment is left
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode); // still logs in (Main)
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{user.Id}")).StatusCode); // no longer visible to the Admin
    }

    [Fact]
    public async Task Admin_put_active_false_behaves_like_delete_and_active_true_is_forbidden_for_shared_users()
    {
        var owner = await _f.OwnerAsync();
        var (locA, adminMail, adminPwd) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(adminMail, adminPwd);
        var shared = await CreateAsync(owner, Mail(), "Cashier", locA, Phase2Helpers.Main);

        var put = await admin.PutAsJsonAsync($"/api/v1/users/{shared.Id}", new { displayName = "Renamed", active = false });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = await owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{shared.Id}", ApiFactory.Json);
        Assert.True(after!.Active);
        Assert.Equal("Renamed", after.DisplayName);
        Assert.Single(after.Locations);

        // Reactivating a user that has assignments elsewhere is Owner only.
        var shared2 = await CreateAsync(owner, Mail(), "Cashier", locA, Phase2Helpers.Uptown);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/v1/users/{shared2.Id}", new { displayName = "x", active = true })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_globally_deactivate_a_user_who_has_no_assignments_elsewhere()
    {
        var owner = await _f.OwnerAsync();
        var (locA, adminMail, adminPwd) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(adminMail, adminPwd);
        var email = Mail();
        var user = await CreateAsync(owner, email, "Cashier", locA);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{user.Id}")).StatusCode);
        Assert.False((await owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json))!.Active);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Owner_deactivate_is_still_global()
    {
        var owner = await _f.OwnerAsync();
        var email = Mail();
        var user = await CreateAsync(owner, email, "Cashier", Phase2Helpers.Main, Phase2Helpers.Uptown);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/users/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Phase2Helpers.Main)).StatusCode);
    }

    [Fact]
    public async Task Admin_sees_only_assignments_at_own_location()
    {
        var owner = await _f.OwnerAsync();
        var (locA, adminMail, adminPwd) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(adminMail, adminPwd);
        var user = await CreateAsync(owner, Mail(), "Manager", locA, Phase2Helpers.Main, Phase2Helpers.Uptown);

        var one = await admin.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json);
        Assert.Equal(locA, one!.Locations.Single().LocationId);
        var list = await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100", ApiFactory.Json);
        Assert.All(list!.Data, u => Assert.All(u.Locations, l => Assert.Equal(locA, l.LocationId)));
        var raw = await (await admin.GetAsync($"/api/v1/users/{user.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Uptown", raw);
        Assert.DoesNotContain("Main Branch", raw);

        var full = await owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json);
        Assert.Equal(3, full!.Locations.Count); // Owner view unchanged
    }

    [Fact]
    public async Task Admin_brings_in_an_existing_user_by_exact_email_and_cannot_touch_other_locations()
    {
        var owner = await _f.OwnerAsync();
        var (locA, adminMail, adminPwd) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var admin = await _f.LoginAsync(adminMail, adminPwd);
        var email = Mail();
        var user = await CreateAsync(owner, email, "Cashier", Phase2Helpers.Main);

        // Not visible, so the by-id endpoints are closed to the Admin (404); another location is 403.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/users/{user.Id}/locations", new { locationId = locA, role = "Cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/users/{user.Id}/locations", new { locationId = Phase2Helpers.Main, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/v1/users/{user.Id}/locations/{Phase2Helpers.Main}")).StatusCode);

        // By exact email at the Admin's own location it works, and the response reveals nothing about other branches.
        var ok = await admin.PostAsJsonAsync($"/api/v1/locations/{locA}/staff", new { email, role = "Manager" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var member = await ok.ReadAsync<StaffMemberDto>();
        Assert.Equal(user.Id, member.UserId);
        Assert.Equal("Manager", member.Role);
        Assert.Equal(2, (await owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json))!.Locations.Count);
        Assert.Single((await admin.GetFromJsonAsync<UserDto>($"/api/v1/users/{user.Id}", ApiFactory.Json))!.Locations);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/locations/{Phase2Helpers.Main}/staff", new { email, role = "Cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/locations/{locA}/staff", new { email = Mail(), role = "Cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/locations/{locA}/staff", new { email = DevDataSeeder.AdminEmail, role = "Cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/locations/{locA}/staff", new { email, role = "Owner" })).StatusCode);

        var cashier = await _f.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/v1/locations/{Phase2Helpers.Main}/staff", new { email, role = "Cashier" })).StatusCode);
    }
}
