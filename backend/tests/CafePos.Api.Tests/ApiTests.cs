using System.Net;
using System.Net.Http.Json;
using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using CafePos.Api.Payments;
using CafePos.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Tests;

public class UnitTests
{
    [Theory]
    [InlineData(112, 12, 12.00)]
    [InlineData(150, 12, 16.07)]   // 150 * 12 / 112 = 16.0714...
    [InlineData(95, 12, 10.18)]    // 10.178... rounds up
    [InlineData(100, 0, 0)]
    public void Vat_is_extracted_from_a_vat_inclusive_total(decimal total, decimal rate, decimal expected) =>
        Assert.Equal(expected, PricingCalculator.ExtractVat(total, rate));

    [Fact]
    public async Task Cash_provider_computes_change_and_rejects_short_tender()
    {
        var cash = new CashPaymentProvider();
        var ok = await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, 200m));
        Assert.True(ok.Succeeded);
        Assert.Equal(50m, ok.ChangeGiven);

        Assert.False((await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, 100m))).Succeeded);
        Assert.False((await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, null))).Succeeded);
    }

    [Fact]
    public void Resolver_rejects_unknown_method()
    {
        var resolver = new PaymentProviderResolver(new IPaymentProvider[] { new CashPaymentProvider() });
        Assert.Equal("cash", resolver.Resolve("CASH").Method);
        Assert.Throws<ApiException>(() => resolver.Resolve("stripe"));
    }
}

[Collection("api")]
public class AuthTests
{
    private readonly ApiFactory _f;
    public AuthTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Login_returns_token_role_and_location()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.ReadAsync<AuthResponse>();
        Assert.Equal("Cashier", auth.User.Role);
        Assert.Equal(DevDataSeeder.MainLocationId, auth.User.Location!.Id);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
    }

    [Fact]
    public async Task Bad_password_is_401_problem_details()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.AdminEmail, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(401, await res.ProblemStatusAsync());
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        var res = await _f.CreateClient().GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Cashier_cannot_write_catalog_but_admin_can()
    {
        var cashier = await _f.CashierAsync();
        var denied = await cashier.PostAsJsonAsync("/api/v1/categories", new { name = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var admin = await _f.AdminAsync();
        var ok = await admin.PostAsJsonAsync("/api/v1/categories", new { name = "Seasonal", sortOrder = 9 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Validation_errors_are_400_problem_details()
    {
        var admin = await _f.AdminAsync();
        var res = await admin.PostAsJsonAsync("/api/v1/categories", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(400, await res.ProblemStatusAsync());
    }

    [Fact]
    public async Task Refresh_rotates_cookie_and_old_cookie_is_rejected()
    {
        var client = _f.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword });
        var oldCookie = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("cafepos_refresh=")).Split(';')[0];
        Assert.Contains("httponly", string.Join(";", login.Headers.GetValues("Set-Cookie")).ToLowerInvariant());

        var refreshed = await client.PostAsync("/api/v1/auth/refresh", null); // cookie container sends the cookie
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", oldCookie);
        var other = _f.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.SendAsync(replay)).StatusCode);
    }
}

[Collection("api")]
public class OrderFlowTests
{
    private readonly ApiFactory _f;
    public OrderFlowTests(ApiFactory f) => _f = f;

    private static object Line(Guid variationId, int qty = 1, params Guid[] modifierIds) =>
        new { itemVariationId = variationId, quantity = qty, modifierIds };

    private async Task<OrderDto> CreateOrderAsync(HttpClient c, params object[] lines)
    {
        var res = await c.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = lines });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.ReadAsync<OrderDto>();
    }

    private async Task<Dictionary<string, decimal>> StockAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json))!.ToDictionary(i => i.Name, i => i.QuantityOnHand!.Value);

    [Fact]
    public async Task Cash_sale_completes_decrements_stock_via_ledger_and_extracts_vat()
    {
        var c = await _f.CashierAsync();
        var latteM = await _f.VariationIdAsync(c, "LAT-M");
        var before = await StockAsync(c);

        var order = await CreateOrderAsync(c, Line(latteM, 2));
        Assert.Equal("Open", order.Status);
        Assert.Equal(300m, order.Total);                 // 2 x 150, VAT-inclusive
        Assert.Equal(32.14m, order.VatAmount);           // 300 * 12 / 112
        Assert.Equal(267.86m, order.VatableSales);

        var done = await c.PostWithKeyAsync($"/api/v1/orders/{order.Id}/complete", new { paymentMethod = "cash", amountTendered = 500 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var completed = await done.ReadAsync<OrderDto>();
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.OrderNumber);
        Assert.Equal(200m, completed.Payments.Single().ChangeGiven);

        var after = await StockAsync(c);
        Assert.Equal(before["Espresso Beans"] - 36, after["Espresso Beans"]);
        Assert.Equal(before["Whole Milk"] - 400, after["Whole Milk"]);
        Assert.Equal(before["Paper Cups"] - 2, after["Paper Cups"]);

        using var db = _f.NewDb(DevDataSeeder.MainLocationId);
        var movements = await db.StockMovements.Where(m => m.SourceDocumentRef == $"order:{order.Id}").ToListAsync();
        Assert.Equal(3, movements.Count);
        Assert.All(movements, m => Assert.Equal(StockMovementReason.Sale, m.Reason));

        var receipt = await c.GetFromJsonAsync<ReceiptDto>($"/api/v1/orders/{order.Id}/receipt", ApiFactory.Json);
        Assert.Equal(completed.OrderNumber, receipt!.OrderNumber);
        Assert.True(receipt.PricesIncludeVat);
        Assert.Equal(300m, receipt.Total);
        Assert.Equal("PHP", receipt.Currency);
    }

    [Fact]
    public async Task Modifiers_add_to_price_and_selection_rules_are_enforced()
    {
        var c = await _f.CashierAsync();
        var latteS = await _f.VariationIdAsync(c, "LAT-S");
        var lists = await c.GetFromJsonAsync<List<ModifierListDto>>("/api/v1/modifier-lists", ApiFactory.Json);
        var milk = lists!.First(l => l.Name == "Milk Type").Modifiers;
        var oat = milk.First(m => m.Name == "Oat Milk").Id;
        var almond = milk.First(m => m.Name == "Almond Milk").Id;

        var order = await CreateOrderAsync(c, Line(latteS, 1, oat));
        Assert.Equal(160m, order.Total); // 130 + 30

        var tooMany = await c.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { Line(latteS, 1, oat, almond) } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
    }

    [Fact]
    public async Task Create_with_same_client_id_is_idempotent()
    {
        var c = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(c, "CRO-1");
        var id = Guid.NewGuid();
        var body = new { id, items = new[] { Line(cro) } };
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/orders", body)).StatusCode);
        var again = await c.PostAsJsonAsync("/api/v1/orders", body);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(id, (await again.ReadAsync<OrderDto>()).Id);
    }

    [Fact]
    public async Task Complete_with_same_idempotency_key_replays_and_never_double_decrements()
    {
        var c = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(c, "CRO-1");
        var before = (await StockAsync(c))["Butter Croissant"];
        var order = await CreateOrderAsync(c, Line(cro, 3));
        var key = Guid.NewGuid().ToString();
        var url = $"/api/v1/orders/{order.Id}/complete";
        var pay = new { paymentMethod = "cash", amountTendered = 1000 };

        var first = await c.PostWithKeyAsync(url, pay, key);
        var second = await c.PostWithKeyAsync(url, pay, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));
        var a = await first.ReadAsync<OrderDto>();
        var b = await second.ReadAsync<OrderDto>();
        Assert.Equal(a.OrderNumber, b.OrderNumber);
        Assert.Equal(a.Payments.Single().Id, b.Payments.Single().Id);

        Assert.Equal(before - 3, (await StockAsync(c))["Butter Croissant"]);
        using var db = _f.NewDb(DevDataSeeder.MainLocationId);
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.SourceDocumentRef == $"order:{order.Id}"));
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.Id));

        // A different key for an already completed order is a conflict, not a second sale.
        var third = await c.PostWithKeyAsync(url, pay, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal(before - 3, (await StockAsync(c))["Butter Croissant"]);
    }

    [Fact]
    public async Task Idempotency_key_is_required_and_cannot_be_reused_across_orders()
    {
        var c = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(c, "CRO-1");
        var o1 = await CreateOrderAsync(c, Line(cro));
        var o2 = await CreateOrderAsync(c, Line(cro));
        var pay = new { paymentMethod = "cash", amountTendered = 500 };

        var missing = await c.PostWithKeyAsync($"/api/v1/orders/{o1.Id}/complete", pay, null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await c.PostWithKeyAsync($"/api/v1/orders/{o1.Id}/complete", pay, key)).StatusCode);
        var reuse = await c.PostWithKeyAsync($"/api/v1/orders/{o2.Id}/complete", pay, key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reuse.StatusCode);
    }

    [Fact]
    public async Task Insufficient_cash_is_422_and_changes_nothing()
    {
        var c = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(c, "CRO-1");
        var before = (await StockAsync(c))["Butter Croissant"];
        var order = await CreateOrderAsync(c, Line(cro));
        var res = await c.PostWithKeyAsync($"/api/v1/orders/{order.Id}/complete", new { paymentMethod = "cash", amountTendered = 10 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("Open", (await c.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json))!.Status);
        Assert.Equal(before, (await StockAsync(c))["Butter Croissant"]);
    }

    [Fact]
    public async Task Unsupported_payment_method_is_rejected()
    {
        var c = await _f.CashierAsync();
        var order = await CreateOrderAsync(c, Line(await _f.VariationIdAsync(c, "CRO-1")));
        var res = await c.PostWithKeyAsync($"/api/v1/orders/{order.Id}/complete", new { paymentMethod = "card" }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Oversell_is_blocked_with_409_and_the_whole_transaction_rolls_back()
    {
        var admin = await _f.AdminAsync();
        // Ingredient A plentiful, ingredient B scarce; one item consumes both.
        var plenty = await (await admin.PostAsJsonAsync("/api/v1/ingredients", new { name = "Plenty " + Guid.NewGuid().ToString("N")[..6], unit = "g", lowStockThreshold = 1, initialQuantity = 1000 })).ReadAsync<IngredientDto>();
        var scarce = await (await admin.PostAsJsonAsync("/api/v1/ingredients", new { name = "Scarce " + Guid.NewGuid().ToString("N")[..6], unit = "g", lowStockThreshold = 1, initialQuantity = 5 })).ReadAsync<IngredientDto>();
        var cats = await admin.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", ApiFactory.Json);
        var sku = "OVS-" + Guid.NewGuid().ToString("N")[..8];
        var item = await (await admin.PostAsJsonAsync("/api/v1/items", new
        {
            categoryId = cats![0].Id, name = "Oversell test",
            variations = new[] { new { name = "One", sku, price = 50 } }
        })).ReadAsync<ItemDto>();
        var variationId = item.Variations.Single().Id;
        var recipe = await admin.PutAsJsonAsync($"/api/v1/variations/{variationId}/recipe", new
        {
            lines = new[] { new { ingredientId = plenty.Id, quantity = 10 }, new { ingredientId = scarce.Id, quantity = 3 } }
        });
        Assert.Equal(HttpStatusCode.OK, recipe.StatusCode);

        var order = await CreateOrderAsync(admin, Line(variationId, 2)); // needs 6 of scarce, only 5 exist
        var res = await admin.PostWithKeyAsync($"/api/v1/orders/{order.Id}/complete", new { paymentMethod = "cash", amountTendered = 500 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(409, await res.ProblemStatusAsync());

        var stock = await StockAsync(admin);
        Assert.Equal(1000, stock[plenty.Name]);   // rolled back, not left at 980
        Assert.Equal(5, stock[scarce.Name]);
        var after = await admin.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", ApiFactory.Json);
        Assert.Equal("Open", after!.Status);
        Assert.Null(after.OrderNumber);

        // A smaller order that fits succeeds (one unit needs 3 of 5).
        var small = await CreateOrderAsync(admin, Line(variationId, 1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithKeyAsync($"/api/v1/orders/{small.Id}/complete", new { paymentMethod = "cash", amountTendered = 50 }, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(2, (await StockAsync(admin))[scarce.Name]);
    }

    [Fact]
    public async Task Concurrent_sales_of_the_last_stock_never_oversell()
    {
        var admin = await _f.AdminAsync();
        var ing = await (await admin.PostAsJsonAsync("/api/v1/ingredients", new { name = "Race " + Guid.NewGuid().ToString("N")[..6], unit = "g", lowStockThreshold = 1, initialQuantity = 6 })).ReadAsync<IngredientDto>();
        var cats = await admin.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", ApiFactory.Json);
        var item = await (await admin.PostAsJsonAsync("/api/v1/items", new
        {
            categoryId = cats![0].Id, name = "Race test", variations = new[] { new { name = "One", sku = "RACE-" + Guid.NewGuid().ToString("N")[..8], price = 10 } }
        })).ReadAsync<ItemDto>();
        var vid = item.Variations.Single().Id;
        await admin.PutAsJsonAsync($"/api/v1/variations/{vid}/recipe", new { lines = new[] { new { ingredientId = ing.Id, quantity = 4 } } });

        // Four parallel orders each needing 4 of the 6 available: exactly one can win.
        var orders = new List<OrderDto>();
        for (var i = 0; i < 4; i++) orders.Add(await CreateOrderAsync(admin, Line(vid)));
        var results = await Task.WhenAll(orders.Select(o =>
            admin.PostWithKeyAsync($"/api/v1/orders/{o.Id}/complete", new { paymentMethod = "cash", amountTendered = 100 }, Guid.NewGuid().ToString())));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2m, (await StockAsync(admin))[ing.Name]);
    }

    [Fact]
    public async Task Cancel_open_order_and_cannot_complete_it_afterwards()
    {
        var c = await _f.CashierAsync();
        var order = await CreateOrderAsync(c, Line(await _f.VariationIdAsync(c, "CRO-1")));
        var cancel = await c.PostAsync($"/api/v1/orders/{order.Id}/cancel", null);
        Assert.Equal("Canceled", (await cancel.ReadAsync<OrderDto>()).Status);
        var res = await c.PostWithKeyAsync($"/api/v1/orders/{order.Id}/complete", new { paymentMethod = "cash", amountTendered = 500 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Add_update_and_remove_lines_recalculate_totals()
    {
        var c = await _f.CashierAsync();
        var latteS = await _f.VariationIdAsync(c, "LAT-S");
        var cro = await _f.VariationIdAsync(c, "CRO-1");
        var order = await CreateOrderAsync(c, Line(latteS));
        var added = await (await c.PostAsJsonAsync($"/api/v1/orders/{order.Id}/items", Line(cro, 2))).ReadAsync<OrderDto>();
        Assert.Equal(130m + 190m, added.Total);
        var croLine = added.Items.First(i => i.Sku == "CRO-1");
        var updated = await (await c.PutAsJsonAsync($"/api/v1/orders/{order.Id}/items/{croLine.Id}", Line(cro, 1))).ReadAsync<OrderDto>();
        Assert.Equal(130m + 95m, updated.Total);
        var removed = await c.DeleteAsync($"/api/v1/orders/{order.Id}/items/{updated.Items.First(i => i.Sku == "CRO-1").Id}");
        Assert.Equal(130m, (await removed.ReadAsync<OrderDto>()).Total);
    }

    [Fact]
    public async Task Cashier_sees_only_own_orders_manager_sees_all_and_lists_are_paged()
    {
        var cashier = await _f.CashierAsync();
        var admin = await _f.AdminAsync();
        var cro = await _f.VariationIdAsync(cashier, "CRO-1");
        var mine = await CreateOrderAsync(cashier, Line(cro));
        var theirs = await CreateOrderAsync(admin, Line(cro));

        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/v1/orders/{theirs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/orders/{mine.Id}")).StatusCode);

        var list = await cashier.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders?pageSize=5", ApiFactory.Json);
        Assert.All(list!.Data, o => Assert.Equal(mine.CashierId, o.CashierId));
        Assert.Equal(1, list.Page);
        Assert.Equal(5, list.PageSize);
        Assert.True(list.TotalCount >= 1);
    }
}

[Collection("api")]
public class InventoryTests
{
    private readonly ApiFactory _f;
    public InventoryTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Low_stock_lists_ingredients_at_or_below_threshold()
    {
        var c = await _f.CashierAsync();
        var low = await c.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients/low-stock", ApiFactory.Json);
        Assert.Contains(low!, i => i.Name == "Tea Bags" && i.IsLowStock == true);
    }

    [Fact]
    public async Task Restock_increases_stock_and_appends_ledger_row()
    {
        var admin = await _f.AdminAsync();
        var ing = await (await admin.PostAsJsonAsync("/api/v1/ingredients", new { name = "Syrup " + Guid.NewGuid().ToString("N")[..6], unit = "ml", lowStockThreshold = 100, initialQuantity = 50 })).ReadAsync<IngredientDto>();
        Assert.True(ing.IsLowStock);

        var res = await admin.PostAsJsonAsync("/api/v1/restocks", new { ingredientId = ing.Id, quantity = 500, unitCost = 0.35, supplier = "Metro Wholesale" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var list = await admin.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json);
        var now = list!.First(i => i.Id == ing.Id);
        Assert.Equal(550m, now.QuantityOnHand);
        Assert.False(now.IsLowStock);

        var movements = await admin.GetFromJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/ingredients/{ing.Id}/movements", ApiFactory.Json);
        Assert.Equal(2, movements!.TotalCount); // opening balance + restock
        Assert.Contains(movements.Data, m => m.Reason == "Restock" && m.QuantityDelta == 500m);
    }

    [Fact]
    public async Task Adjustment_cannot_take_stock_negative_and_cashier_cannot_restock()
    {
        var admin = await _f.AdminAsync();
        var ing = await (await admin.PostAsJsonAsync("/api/v1/ingredients", new { name = "Adj " + Guid.NewGuid().ToString("N")[..6], unit = "g", lowStockThreshold = 1, initialQuantity = 10 })).ReadAsync<IngredientDto>();
        var bad = await admin.PostAsJsonAsync($"/api/v1/ingredients/{ing.Id}/adjustments", new { quantityDelta = -11, reason = "Waste", note = "spilled" });
        Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        var good = await admin.PostAsJsonAsync($"/api/v1/ingredients/{ing.Id}/adjustments", new { quantityDelta = -4, reason = "Waste", note = "spilled" });
        Assert.Equal(HttpStatusCode.Created, good.StatusCode);

        var cashier = await _f.CashierAsync();
        var denied = await cashier.PostAsJsonAsync("/api/v1/restocks", new { ingredientId = ing.Id, quantity = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Stock_ledger_is_append_only()
    {
        using var db = _f.NewDb(DevDataSeeder.MainLocationId);
        var movement = await db.StockMovements.FirstAsync();
        movement.Note = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        using var db2 = _f.NewDb(DevDataSeeder.MainLocationId);
        db2.StockMovements.Remove(await db2.StockMovements.FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Vat_rate_is_configurable_per_location_by_admin()
    {
        var admin = await _f.AdminAsync();
        var cashier = await _f.CashierAsync();
        var denied = await cashier.PutAsJsonAsync($"/api/v1/locations/{DevDataSeeder.MainLocationId}", new { name = "X", timeZone = "Asia/Manila", vatRatePercent = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var cur = await admin.GetFromJsonAsync<LocationDto>("/api/v1/locations/current", ApiFactory.Json);
        Assert.Equal(12m, cur!.VatRatePercent);
        var res = await admin.PutAsJsonAsync($"/api/v1/locations/{cur.Id}", new { name = cur.Name, address = cur.Address, timeZone = cur.TimeZone, contactPhone = cur.ContactPhone, vatRatePercent = 12 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

[Collection("api")]
public class LocationIsolationTests
{
    private readonly ApiFactory _f;
    public LocationIsolationTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Global_query_filters_hide_other_locations_rows_at_the_data_layer()
    {
        var (otherId, _, _) = await _f.CreateOtherLocationAsync();
        var mainOrderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        Guid ingredientId;
        using (var sys = _f.NewDb(null)) // system context: no location, writes unrestricted
        {
            ingredientId = (await sys.Ingredients.FirstAsync()).Id;
            sys.Orders.Add(new Order { Id = mainOrderId, LocationId = DevDataSeeder.MainLocationId, CashierId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, VatRatePercent = 12 });
            sys.Orders.Add(new Order { Id = otherOrderId, LocationId = otherId, CashierId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, VatRatePercent = 12 });
            sys.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), LocationId = otherId, IngredientId = ingredientId, QuantityDelta = 1, Reason = StockMovementReason.Restock, CreatedAt = DateTime.UtcNow });
            await sys.SaveChangesAsync();
        }

        using var main = _f.NewDb(DevDataSeeder.MainLocationId);
        Assert.NotNull(await main.Orders.FirstOrDefaultAsync(o => o.Id == mainOrderId));
        Assert.Null(await main.Orders.FirstOrDefaultAsync(o => o.Id == otherOrderId));
        Assert.DoesNotContain(await main.Orders.ToListAsync(), o => o.LocationId != DevDataSeeder.MainLocationId);
        Assert.DoesNotContain(await main.StockMovements.ToListAsync(), m => m.LocationId != DevDataSeeder.MainLocationId);
        Assert.DoesNotContain(await main.IngredientStocks.ToListAsync(), s => s.LocationId != DevDataSeeder.MainLocationId);

        using var other = _f.NewDb(otherId);
        Assert.Null(await other.Orders.FirstOrDefaultAsync(o => o.Id == mainOrderId));
        Assert.NotNull(await other.Orders.FirstOrDefaultAsync(o => o.Id == otherOrderId));
        Assert.DoesNotContain(await other.StockMovements.ToListAsync(), m => m.LocationId != otherId);

        using var none = _f.NewDb(null);
        Assert.Empty(await none.Orders.ToListAsync()); // no active location matches nothing

        // Explicit, opt-in escape hatch for admin-gated code paths only.
        Assert.True((await main.Orders.IgnoreQueryFilters().Select(o => o.LocationId).Distinct().CountAsync()) >= 2);
    }

    [Fact]
    public async Task A_location_context_cannot_write_to_another_location()
    {
        var (otherId, _, _) = await _f.CreateOtherLocationAsync();
        using var main = _f.NewDb(DevDataSeeder.MainLocationId);
        main.Orders.Add(new Order { Id = Guid.NewGuid(), LocationId = otherId, CashierId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<InvalidOperationException>(() => main.SaveChangesAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_location()
    {
        using var main = _f.NewDb(DevDataSeeder.MainLocationId);
        var o = new Order { Id = Guid.NewGuid(), CashierId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        main.Orders.Add(o);
        await main.SaveChangesAsync();
        Assert.Equal(DevDataSeeder.MainLocationId, o.LocationId);
    }

    [Fact]
    public async Task Api_user_at_another_location_cannot_see_or_complete_this_locations_orders_or_stock()
    {
        var cashierA = await _f.CashierAsync();
        var cro = await _f.VariationIdAsync(cashierA, "CRO-1");
        var created = await cashierA.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { new { itemVariationId = cro, quantity = 1 } } });
        var orderA = await created.ReadAsync<OrderDto>();

        var (otherId, email, password) = await _f.CreateOtherLocationAsync(Roles.Manager);
        var userB = await _f.LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/v1/orders/{orderA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/v1/orders/{orderA.Id}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.PostAsync($"/api/v1/orders/{orderA.Id}/cancel", null)).StatusCode);
        var complete = await userB.PostWithKeyAsync($"/api/v1/orders/{orderA.Id}/complete", new { paymentMethod = "cash", amountTendered = 500 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);

        var list = await userB.GetFromJsonAsync<PagedResult<OrderDto>>("/api/v1/orders", ApiFactory.Json);
        Assert.Empty(list!.Data);

        // Reusing location A's order id at location B is refused without leaking its existence.
        var steal = await userB.PostAsJsonAsync("/api/v1/orders", new { id = orderA.Id, items = new[] { new { itemVariationId = cro, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Conflict, steal.StatusCode);

        // Location B sees its own stock rows (100 each), not location A's.
        var stockB = await userB.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json);
        Assert.All(stockB!, i => Assert.Equal(100m, i.QuantityOnHand));

        // And A is untouched by B's activity: A's order is still open.
        Assert.Equal("Open", (await cashierA.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{orderA.Id}", ApiFactory.Json))!.Status);

        // Location B has its own receipt numbering and its own sale does not touch A's stock.
        var beforeA = (await cashierA.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json))!.First(i => i.Name == "Butter Croissant").QuantityOnHand;
        var orderB = await (await userB.PostAsJsonAsync("/api/v1/orders", new { id = Guid.NewGuid(), items = new[] { new { itemVariationId = cro, quantity = 2 } } })).ReadAsync<OrderDto>();
        var doneB = await userB.PostWithKeyAsync($"/api/v1/orders/{orderB.Id}/complete", new { paymentMethod = "cash", amountTendered = 500 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, doneB.StatusCode);
        Assert.Equal(1, (await doneB.ReadAsync<OrderDto>()).OrderNumber);
        var afterA = (await cashierA.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json))!.First(i => i.Name == "Butter Croissant").QuantityOnHand;
        Assert.Equal(beforeA, afterA);
        var afterB = (await userB.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients", ApiFactory.Json))!.First(i => i.Name == "Butter Croissant").QuantityOnHand;
        Assert.Equal(98m, afterB);
        _ = otherId;
    }

    [Fact]
    public async Task Tokens_for_a_location_the_user_is_not_assigned_to_cannot_be_obtained()
    {
        var (otherId, _, _) = await _f.CreateOtherLocationAsync();
        var res = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = DevDataSeeder.CashierEmail, password = DevDataSeeder.CashierPassword, locationId = otherId });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
