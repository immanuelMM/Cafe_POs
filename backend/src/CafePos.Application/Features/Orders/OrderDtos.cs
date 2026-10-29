namespace CafePos.Application.Features.Orders;

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
