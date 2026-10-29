using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// Development seed data (runs only when Database:SeedDevData is true, and only on an empty database).
/// The credentials below are DEV ONLY and documented in docs/api/phase1-api.md and docs/api/phase2-api.md.
/// </summary>
public static class DevDataSeeder
{
    public static readonly Guid MainLocationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UptownLocationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    /// <summary>Owner (unrestricted, no assignments). Was the single Admin in Phase 1.</summary>
    public const string AdminEmail = "admin@cafe.local";
    public const string AdminPassword = "Admin#Dev12345";
    public const string CashierEmail = "cashier@cafe.local";
    public const string CashierPassword = "Cashier#Dev12345";
    /// <summary>Manager assigned to BOTH locations (exercises the multi-location login / switch flow).</summary>
    public const string ManagerEmail = "manager@cafe.local";
    public const string ManagerPassword = "Manager#Dev12345";
    /// <summary>Cashier assigned to the Uptown branch only.</summary>
    public const string UptownCashierEmail = "cashier.uptown@cafe.local";
    public const string UptownCashierPassword = "Cashier#Dev12345";

    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<CafeDbContext>();
        if (await db.Locations.AnyAsync()) return;

        var location = new Location
        {
            Id = MainLocationId,
            Name = "Brew & Bean - Main Branch",
            Address = "123 Rizal Avenue, Makati City, Metro Manila",
            TimeZone = "Asia/Manila",
            ContactPhone = "+63 2 8123 4567",
            VatRatePercent = 12m
        };
        var uptown = new Location
        {
            Id = UptownLocationId,
            Name = "Brew & Bean - Uptown Branch",
            Address = "88 Katipunan Avenue, Quezon City, Metro Manila",
            TimeZone = "Asia/Manila",
            ContactPhone = "+63 2 8765 4321",
            VatRatePercent = 12m
        };
        db.Locations.AddRange(location, uptown);
        await db.SaveChangesAsync();

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await CreateUser(users, AdminEmail, AdminPassword, "Alex Admin", isOwner: true);
        var cashier = await CreateUser(users, CashierEmail, CashierPassword, "Casey Cashier");
        var manager = await CreateUser(users, ManagerEmail, ManagerPassword, "Morgan Manager");
        var uptownCashier = await CreateUser(users, UptownCashierEmail, UptownCashierPassword, "Uma Uptown");
        _ = owner; // Owners need no StaffAssignment rows
        db.StaffAssignments.Add(new StaffAssignment { UserId = cashier.Id, LocationId = location.Id, Role = Roles.Cashier });
        db.StaffAssignments.Add(new StaffAssignment { UserId = manager.Id, LocationId = location.Id, Role = Roles.Manager });
        db.StaffAssignments.Add(new StaffAssignment { UserId = manager.Id, LocationId = uptown.Id, Role = Roles.Manager });
        db.StaffAssignments.Add(new StaffAssignment { UserId = uptownCashier.Id, LocationId = uptown.Id, Role = Roles.Cashier });

        // Ingredients (name, unit, low-stock threshold, opening stock)
        var beans = Ing("Espresso Beans", "g", 1000, 5000);
        var milk = Ing("Whole Milk", "ml", 3000, 20000);
        var teaBags = Ing("Tea Bags", "each", 10, 8); // intentionally below threshold: demonstrates low-stock
        var croissant = Ing("Butter Croissant", "each", 5, 20);
        var cups = Ing("Paper Cups", "each", 50, 200);
        var ingredients = new[] { beans, milk, teaBags, croissant, cups };
        db.Ingredients.AddRange(ingredients.Select(i => i.ing));
        await db.SaveChangesAsync();
        foreach (var (ing, qty) in ingredients)
        {
            // Independent stock + ledger per location. Uptown starts with different levels (Tea Bags is healthy there).
            foreach (var (loc, opening) in new[] { (location, qty), (uptown, ing.Name == "Tea Bags" ? 60m : Math.Round(qty * 0.6m)) })
            {
                db.IngredientStocks.Add(new IngredientStock { LocationId = loc.Id, IngredientId = ing.Id, QuantityOnHand = opening });
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(), LocationId = loc.Id, IngredientId = ing.Id, QuantityDelta = opening,
                    Reason = StockMovementReason.Adjustment, Note = "Opening balance (seed)", CreatedAt = DateTime.UtcNow
                });
            }
        }

        // Categories
        var coffee = new Category { Id = Guid.NewGuid(), Name = "Coffee", SortOrder = 1 };
        var tea = new Category { Id = Guid.NewGuid(), Name = "Tea", SortOrder = 2 };
        var pastry = new Category { Id = Guid.NewGuid(), Name = "Pastries", SortOrder = 3 };
        db.Categories.AddRange(coffee, tea, pastry);

        // Modifier lists
        var milkChoice = new ModifierList { Id = Guid.NewGuid(), Name = "Milk Type", MinSelections = 0, MaxSelections = 1 };
        milkChoice.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = milkChoice.Id, Name = "Oat Milk", PriceDelta = 30m });
        milkChoice.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = milkChoice.Id, Name = "Almond Milk", PriceDelta = 30m });
        var extras = new ModifierList { Id = Guid.NewGuid(), Name = "Extras", MinSelections = 0, MaxSelections = 2 };
        extras.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = extras.Id, Name = "Extra Shot", PriceDelta = 40m });
        extras.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = extras.Id, Name = "Vanilla Syrup", PriceDelta = 25m });
        db.ModifierLists.AddRange(milkChoice, extras);

        // Items, variations (VAT-inclusive PHP prices) and recipes
        var latte = Item(db, coffee, "Latte", "Espresso with steamed milk", 1, milkChoice, extras);
        Var(db, latte, "Small", "LAT-S", 130m, (beans.ing, 14), (milk.ing, 150), (cups.ing, 1));
        Var(db, latte, "Medium", "LAT-M", 150m, (beans.ing, 18), (milk.ing, 200), (cups.ing, 1));
        Var(db, latte, "Large", "LAT-L", 170m, (beans.ing, 22), (milk.ing, 260), (cups.ing, 1));

        var espresso = Item(db, coffee, "Espresso", "Rich double-strength shot", 2, extras);
        Var(db, espresso, "Single", "ESP-1", 90m, (beans.ing, 9), (cups.ing, 1));
        Var(db, espresso, "Double", "ESP-2", 120m, (beans.ing, 18), (cups.ing, 1));

        var capp = Item(db, coffee, "Cappuccino", "Equal parts espresso, milk and foam", 3, milkChoice, extras);
        Var(db, capp, "Medium", "CAP-M", 150m, (beans.ing, 18), (milk.ing, 120), (cups.ing, 1));

        var icedTea = Item(db, tea, "Iced Tea", "House black tea, brewed daily", 1);
        Var(db, icedTea, "Regular", "TEA-R", 100m, (teaBags.ing, 1), (cups.ing, 1));

        var croissantItem = Item(db, pastry, "Butter Croissant", "Baked fresh each morning", 1);
        Var(db, croissantItem, "Each", "CRO-1", 95m, (croissant.ing, 1));

        await db.SaveChangesAsync();
    }

    private static (Ingredient ing, decimal qty) Ing(string name, string unit, decimal threshold, decimal stock) =>
        (new Ingredient { Id = Guid.NewGuid(), Name = name, Unit = unit, LowStockThreshold = threshold }, stock);

    private static Item Item(CafeDbContext db, Category cat, string name, string description, int sort, params ModifierList[] lists)
    {
        var item = new Item { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = name, Description = description, SortOrder = sort };
        foreach (var l in lists) item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = l.Id });
        db.Items.Add(item);
        return item;
    }

    private static void Var(CafeDbContext db, Item item, string name, string sku, decimal price, params (Ingredient ing, decimal qty)[] recipe)
    {
        var v = new ItemVariation { Id = Guid.NewGuid(), ItemId = item.Id, Name = name, Sku = sku, Price = price };
        foreach (var (ing, qty) in recipe)
            v.Recipe.Add(new RecipeLine { Id = Guid.NewGuid(), ItemVariationId = v.Id, IngredientId = ing.Id, Quantity = qty });
        item.Variations.Add(v);
    }

    private static async Task<ApplicationUser> CreateUser(UserManager<ApplicationUser> users, string email, string password, string displayName, bool isOwner = false)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName, IsOwner = isOwner };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException("Seeding user failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }
}
