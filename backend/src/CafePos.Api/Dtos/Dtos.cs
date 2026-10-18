using System.ComponentModel.DataAnnotations;

namespace CafePos.Api.Dtos;

// ---- Auth ----
public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    /// <summary>Optional; required only when the user is assigned to several locations.</summary>
    public Guid? LocationId { get; init; }
}

public record LocationRef(Guid Id, string Name);
public record LocationRoleDto(Guid LocationId, string LocationName, string Role);
public record UserInfoDto(Guid Id, string Email, string DisplayName, string Role, LocationRef Location, IReadOnlyList<LocationRoleDto> Locations);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfoDto User);

// ---- Locations ----
public record LocationDto(Guid Id, string Name, string? Address, string TimeZone, string? ContactPhone, decimal VatRatePercent, bool Active);

public record UpdateLocationRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [MaxLength(500)] public string? Address { get; init; }
    [Required, MaxLength(100)] public string TimeZone { get; init; } = "Asia/Manila";
    [MaxLength(50)] public string? ContactPhone { get; init; }
    [Range(0, 100)] public decimal VatRatePercent { get; init; } = 12m;
}

// ---- Catalog ----
public record CategoryDto(Guid Id, string Name, int SortOrder, bool Active);

public record UpsertCategoryRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public bool? Active { get; init; }
}

public record VariationDto(Guid Id, Guid ItemId, string Name, string Sku, decimal Price, bool Active);

public record ItemDto(Guid Id, Guid CategoryId, string Name, string? Description, int SortOrder, bool Active,
    IReadOnlyList<VariationDto> Variations, IReadOnlyList<Guid> ModifierListIds);

public record UpsertVariationRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Required, MaxLength(64)] public string Sku { get; init; } = "";
    [Range(0, 1_000_000)] public decimal Price { get; init; }
    public bool? Active { get; init; }
}

public record UpsertItemRequest
{
    [Required] public Guid CategoryId { get; init; }
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [MaxLength(500)] public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool? Active { get; init; }
    public List<Guid>? ModifierListIds { get; init; }
    /// <summary>Required (at least one) on create; ignored on update (use the variation endpoints).</summary>
    public List<UpsertVariationRequest>? Variations { get; init; }
}

public record ModifierDto(Guid Id, string Name, decimal PriceDelta, bool Active);
public record ModifierListDto(Guid Id, string Name, int MinSelections, int MaxSelections, bool Active, IReadOnlyList<ModifierDto> Modifiers);

public record UpsertModifierRequest
{
    /// <summary>Existing modifier id to update; omit to create.</summary>
    public Guid? Id { get; init; }
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Range(-100_000, 100_000)] public decimal PriceDelta { get; init; }
}

public record UpsertModifierListRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Range(0, 50)] public int MinSelections { get; init; }
    [Range(1, 50)] public int MaxSelections { get; init; } = 1;
    public List<UpsertModifierRequest> Modifiers { get; init; } = new();
}

// ---- Inventory ----
public record IngredientDto(Guid Id, string Name, string Unit, decimal LowStockThreshold, bool Active, decimal QuantityOnHand, bool IsLowStock);

public record CreateIngredientRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Required, MaxLength(20)] public string Unit { get; init; } = "each";
    [Range(0, 1_000_000_000)] public decimal LowStockThreshold { get; init; }
    [Range(0, 1_000_000_000)] public decimal InitialQuantity { get; init; }
}

public record UpdateIngredientRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Required, MaxLength(20)] public string Unit { get; init; } = "each";
    [Range(0, 1_000_000_000)] public decimal LowStockThreshold { get; init; }
    public bool Active { get; init; } = true;
}

public record StockMovementDto(Guid Id, Guid IngredientId, decimal QuantityDelta, string Reason, string? SourceDocumentRef,
    string? Note, string? Supplier, decimal? UnitCost, DateTime CreatedAt, Guid? UserId);

public record RestockRequest
{
    [Required] public Guid IngredientId { get; init; }
    [Range(0.001, 1_000_000_000)] public decimal Quantity { get; init; }
    [Range(0, 1_000_000_000)] public decimal? UnitCost { get; init; }
    [MaxLength(200)] public string? Supplier { get; init; }
    [MaxLength(500)] public string? Note { get; init; }
}

public record AdjustmentRequest
{
    /// <summary>Signed change. Non-zero.</summary>
    public decimal QuantityDelta { get; init; }
    /// <summary>"Adjustment" (default) or "Waste" (waste must be negative).</summary>
    public string Reason { get; init; } = "Adjustment";
    [Required, MaxLength(500)] public string Note { get; init; } = "";
}

public record RecipeLineDto(Guid IngredientId, string IngredientName, string Unit, decimal Quantity);
public record RecipeDto(Guid VariationId, IReadOnlyList<RecipeLineDto> Lines);

public record SetRecipeLine
{
    [Required] public Guid IngredientId { get; init; }
    [Range(0.001, 1_000_000)] public decimal Quantity { get; init; }
}

public record SetRecipeRequest
{
    public List<SetRecipeLine> Lines { get; init; } = new();
}

// ---- Orders ----
public record OrderLineRequest
{
    [Required] public Guid ItemVariationId { get; init; }
    [Range(1, 100)] public int Quantity { get; init; } = 1;
    public List<Guid>? ModifierIds { get; init; }
    [MaxLength(500)] public string? Note { get; init; }
}

public record CreateOrderRequest
{
    /// <summary>Client-generated order id (UUID). Re-posting the same id returns the existing order.</summary>
    [Required] public Guid Id { get; init; }
    [MaxLength(500)] public string? Note { get; init; }
    public List<OrderLineRequest> Items { get; init; } = new();
}

public record CompleteOrderRequest
{
    [Required] public string PaymentMethod { get; init; } = "cash";
    [Range(0, 100_000_000)] public decimal? AmountTendered { get; init; }
}

public record OrderItemModifierDto(Guid ModifierId, string Name, decimal PriceDelta);

public record OrderItemDto(Guid Id, Guid ItemVariationId, string ItemName, string VariationName, string Sku, decimal UnitPrice,
    int Quantity, decimal LineTotal, string? Note, IReadOnlyList<OrderItemModifierDto> Modifiers);

public record PaymentDto(Guid Id, string Method, string Status, decimal Amount, decimal? AmountTendered, decimal? ChangeGiven, DateTime CreatedAt);

public record OrderDto(Guid Id, int? OrderNumber, string Status, Guid LocationId, Guid CashierId, string CashierName, string? Note,
    decimal VatRatePercent, decimal Total, decimal VatableSales, decimal VatAmount, DateTime CreatedAt, DateTime? CompletedAt,
    DateTime? CanceledAt, IReadOnlyList<OrderItemDto> Items, IReadOnlyList<PaymentDto> Payments);

public record ReceiptLineDto(string Description, string? Modifiers, int Quantity, decimal UnitPrice, decimal LineTotal);

public record ReceiptDto(
    string LocationName, string? LocationAddress, string? LocationPhone, int OrderNumber, Guid OrderId, DateTime CompletedAt,
    string TimeZone, string CashierName, IReadOnlyList<ReceiptLineDto> Lines,
    bool PricesIncludeVat, decimal VatRatePercent, decimal VatableSales, decimal VatAmount, decimal Total,
    string PaymentMethod, decimal AmountTendered, decimal ChangeGiven, string Currency);
