using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Orders;

internal sealed class OrderLineRequestValidator : RequestValidator<OrderLineRequest>
{
    public OrderLineRequestValidator()
    {
        RuleFor(x => x.ItemVariationId).SchemaRequired();
        RuleFor(x => x.Quantity).Between(1, 100);
        RuleFor(x => x.Note).MaxLen(500);
    }
}

internal sealed class CreateOrderRequestValidator : RequestValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Id).SchemaRequired();
        RuleFor(x => x.Note).MaxLen(500);
        RuleFor(x => x.Items).IsPresent();
        RuleForEach(x => x.Items).SetValidator(new OrderLineRequestValidator());
    }
}

internal sealed class CompleteOrderRequestValidator : RequestValidator<CompleteOrderRequest>
{
    public CompleteOrderRequestValidator()
    {
        RuleFor(x => x.PaymentMethod).IsRequired();
        RuleFor(x => x.AmountTendered).Between(0m, 100_000_000m);
    }
}
