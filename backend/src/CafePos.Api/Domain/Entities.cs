using Microsoft.AspNetCore.Identity;

namespace CafePos.Api.Domain;

public static class Roles
{
    public const string Cashier = "Cashier";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    /// <summary>Unrestricted across all locations. A user flag (ApplicationUser.IsOwner), not a StaffAssignment role.</summary>
    public const string Owner = "Owner";
    /// <summary>Comma list for [Authorize(Roles = ...)]: Manager, Admin and Owner.</summary>
    public const string ManagerUp = "Manager,Admin,Owner";
    /// <summary>Admin and Owner.</summary>
    public const string AdminUp = "Admin,Owner";
    /// <summary>Roles that can be stored in a StaffAssignment.</summary>
    public static readonly string[] Assignable = { Cashier, Manager, Admin };
    public static readonly string[] All = { Cashier, Manager, Admin, Owner };
}

public enum OrderStatus { Open, Completed, Canceled }
public enum PaymentStatus { Succeeded, Failed }
public enum StockMovementReason { Sale, Restock, Adjustment, Waste }

/// <summary>Entities carrying a LocationId are filtered by EF global query filters.</summary>
public interface ILocationScoped { Guid LocationId { get; set; } }

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string TimeZone { get; set; } = "Asia/Manila";
    public string? ContactPhone { get; set; }
    /// <summary>Stripe Terminal's own Location object id (not this entity); unused until the Stripe phase.</summary>
    public string? StripeTerminalLocationId { get; set; }
    /// <summary>VAT percent (12 = 12%). Catalog prices are VAT-INCLUSIVE; see PricingCalculator.</summary>
    public decimal VatRatePercent { get; set; } = 12m;
    public bool Active { get; set; } = true;
    /// <summary>Per-location receipt counter; incremented atomically when an order completes.</summary>
    public int LastOrderNumber { get; set; }
}

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
    /// <summary>Unrestricted: sees/acts on every location, no StaffAssignment rows needed.</summary>
    public bool IsOwner { get; set; }
}

/// <summary>A user role at a location (plan 4.8). One role per (user, location).</summary>
public class StaffAssignment
{
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }
    public string Role { get; set; } = Roles.Cashier;
    public ApplicationUser? User { get; set; }
    public Location? Location { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Active location the session was created for; null for an Owner's cross-location session.</summary>
    public Guid? LocationId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

// ---- Catalog (global / shared across locations) ----
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}

public class Item
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public Category? Category { get; set; }
    public List<ItemVariation> Variations { get; set; } = new();
    public List<ItemModifierList> ModifierLists { get; set; } = new();
}

public class ItemVariation
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    /// <summary>VAT-inclusive selling price in PHP.</summary>
    public decimal Price { get; set; }
    public bool Active { get; set; } = true;
    public Item? Item { get; set; }
    public List<RecipeLine> Recipe { get; set; } = new();
}

/// <summary>
/// Optional per-location tweak of a global variation (plan 4.2). Not ILocationScoped: it is catalog configuration that an Owner
/// may edit for any location, so access is checked explicitly in the controller.
/// </summary>
public class ItemVariationLocationOverride
{
    public Guid ItemVariationId { get; set; }
    public Guid LocationId { get; set; }
    /// <summary>VAT-inclusive price at this location; null inherits ItemVariation.Price.</summary>
    public decimal? PriceOverride { get; set; }
    /// <summary>false = sales of this variation at this location do not consume recipe stock; null inherits (tracked).</summary>
    public bool? TrackInventoryOverride { get; set; }
    /// <summary>Stored for the location; not yet enforced anywhere (ingredient thresholds stay global).</summary>
    public decimal? LowStockThresholdOverride { get; set; }
}

public class ModifierList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public bool Active { get; set; } = true;
    public List<Modifier> Modifiers { get; set; } = new();
}

public class Modifier
{
    public Guid Id { get; set; }
    public Guid ModifierListId { get; set; }
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
    public bool Active { get; set; } = true;
}

public class ItemModifierList
{
    public Guid ItemId { get; set; }
    public Guid ModifierListId { get; set; }
    public ModifierList? ModifierList { get; set; }
}

// ---- Inventory ----
public class Ingredient
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "each";
    public decimal LowStockThreshold { get; set; }
    public bool Active { get; set; } = true;
}

public class RecipeLine
{
    public Guid Id { get; set; }
    public Guid ItemVariationId { get; set; }
    public Guid IngredientId { get; set; }
    /// <summary>Amount consumed per ONE unit sold, in the unit of the ingredient.</summary>
    public decimal Quantity { get; set; }
    public Ingredient? Ingredient { get; set; }
}

/// <summary>Running stock level per (location, ingredient). Decremented only via conditional UPDATE alongside a StockMovement.</summary>
public class IngredientStock : ILocationScoped
{
    public Guid LocationId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public Ingredient? Ingredient { get; set; }
}

/// <summary>Append-only ledger. Never updated or deleted (enforced in CafeDbContext.SaveChanges).</summary>
public class StockMovement : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal QuantityDelta { get; set; }
    public StockMovementReason Reason { get; set; }
    public string? SourceDocumentRef { get; set; }
    public string? Note { get; set; }
    public string? Supplier { get; set; }
    public decimal? UnitCost { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UserId { get; set; }
}

// ---- Orders & payments ----
public class Order : ILocationScoped
{
    /// <summary>Client-generated GUID.</summary>
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    /// <summary>Per-location receipt number, assigned on completion.</summary>
    public int? OrderNumber { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public Guid CashierId { get; set; }
    public string CashierName { get; set; } = "";
    public string? Note { get; set; }
    public decimal VatRatePercent { get; set; }
    /// <summary>VAT-inclusive total (sum of line totals).</summary>
    public decimal Total { get; set; }
    /// <summary>VAT portion contained within Total.</summary>
    public decimal VatAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

public class OrderItem : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ItemVariationId { get; set; }
    public string ItemName { get; set; } = "";
    public string VariationName { get; set; } = "";
    public string Sku { get; set; } = "";
    /// <summary>Variation price plus modifier deltas, VAT-inclusive.</summary>
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public List<OrderItemModifier> Modifiers { get; set; } = new();
}

public class OrderItemModifier : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ModifierId { get; set; }
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
}

public class Payment : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid LocationId { get; set; }
    public string Method { get; set; } = "cash";
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? AmountTendered { get; set; }
    public decimal? ChangeGiven { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IdempotencyRecord : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string Key { get; set; } = "";
    public Guid OrderId { get; set; }
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
