using CafePos.Application.Features.Locations;
using System.ComponentModel.DataAnnotations;
using CafePos.Domain;

namespace CafePos.Api.Dtos;

// ---- Auth ----
public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    /// <summary>Optional; required only when the user is assigned to several locations.</summary>
    public Guid? LocationId { get; init; }
}

/// <summary>Location is null for an Owner's cross-location session. Locations: assignments (all active locations for an Owner).</summary>
public record UserInfoDto(Guid Id, string Email, string DisplayName, string Role, bool IsOwner, LocationRef? Location, IReadOnlyList<LocationRoleDto> Locations);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfoDto User);

public record SwitchLocationRequest
{
    /// <summary>Target location. Owners may send null for the cross-location session; everyone else must send a location.</summary>
    public Guid? LocationId { get; init; }
}

// ---- Branding ----
public record BrandingDto(string BrandName, string? Tagline, string PrimaryColor, string? LogoUrl, string? TarpaulinUrl, string? TarpaulinContentType,
    string? SecondaryColor, string? TertiaryColor, bool Gradient, int GradientAngle);

public record UpdateBrandingRequest
{
    [Required, MaxLength(100)] public string BrandName { get; init; } = "";
    [MaxLength(200)] public string? Tagline { get; init; }
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Primary color must look like #RRGGBB.")] public string PrimaryColor { get; init; } = "";
    /// <summary>Optional; null or empty clears. Empty strings are allowed by the pattern so the client can clear.</summary>
    [RegularExpression("^(#[0-9a-fA-F]{6})?$", ErrorMessage = "Secondary color must look like #RRGGBB.")] public string? SecondaryColor { get; init; }
    [RegularExpression("^(#[0-9a-fA-F]{6})?$", ErrorMessage = "Tertiary color must look like #RRGGBB.")] public string? TertiaryColor { get; init; }
    public bool Gradient { get; init; }
    [Range(0, 360)] public int GradientAngle { get; init; } = BrandSettings.DefaultGradientAngle;
}

// ---- Time clock ----
public record TimeClockStaffDto(Guid UserId, string DisplayName, string Role, bool HasPin, bool ClockedIn, DateTime? OpenSince);
public record TimeEntryDto(Guid Id, Guid UserId, string DisplayName, Guid LocationId, DateTime ClockInAt, DateTime? ClockOutAt, decimal? Hours);
public record PunchResultDto(string Action, TimeEntryDto Entry);

public record PunchRequest
{
    [Required] public Guid? UserId { get; init; }
    [Required] public string Pin { get; init; } = "";
}

/// <summary>Design-studio document. Document is opaque JSON owned by the frontend editor.</summary>
public record BrandDesignDto(string Kind, System.Text.Json.JsonElement Document, DateTime UpdatedAt);
public record SaveBrandDesignRequest
{
    [Required] public System.Text.Json.JsonElement Document { get; init; }
}

// ---- Locations ----

// ---- Users ----

// ---- Variation location overrides ----

// ---- Catalog ----

// ---- Inventory ----

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
