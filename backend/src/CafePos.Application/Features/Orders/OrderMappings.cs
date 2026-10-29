using CafePos.Domain;

namespace CafePos.Application.Features.Orders;

public static class OrderMappings
{
    public static OrderDto ToDto(this Order o) => new(
        o.Id, o.OrderNumber, o.Status.ToString(), o.LocationId, o.CashierId, o.CashierName, o.Note, o.VatRatePercent,
        o.Total, o.Total - o.VatAmount, o.VatAmount, o.CreatedAt, o.CompletedAt, o.CanceledAt,
        o.Items.Select(i => new OrderItemDto(i.Id, i.ItemVariationId, i.ItemName, i.VariationName, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal, i.Note,
            i.Modifiers.Select(m => new OrderItemModifierDto(m.ModifierId, m.Name, m.PriceDelta)).ToList())).ToList(),
        o.Payments.Select(p => new PaymentDto(p.Id, p.Method, p.Status.ToString(), p.Amount, p.AmountTendered, p.ChangeGiven, p.CreatedAt)).ToList());
}
