using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Catalog;

internal sealed class UpsertCategoryRequestValidator : RequestValidator<UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
    }
}

internal sealed class UpsertVariationRequestValidator : RequestValidator<UpsertVariationRequest>
{
    public UpsertVariationRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Sku).IsRequired().MaxLen(64);
        RuleFor(x => x.Price).Between(0m, 1_000_000m);
    }
}

internal sealed class UpsertItemRequestValidator : RequestValidator<UpsertItemRequest>
{
    public UpsertItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).SchemaRequired();
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Description).MaxLen(500);
        RuleForEach(x => x.Variations).SetValidator(new UpsertVariationRequestValidator());
    }
}

internal sealed class UpsertModifierRequestValidator : RequestValidator<UpsertModifierRequest>
{
    public UpsertModifierRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.PriceDelta).Between(-100_000m, 100_000m);
    }
}

internal sealed class UpsertModifierListRequestValidator : RequestValidator<UpsertModifierListRequest>
{
    public UpsertModifierListRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.MinSelections).Between(0, 50);
        RuleFor(x => x.MaxSelections).Between(1, 50);
        RuleFor(x => x.Modifiers).IsPresent();
        RuleForEach(x => x.Modifiers).SetValidator(new UpsertModifierRequestValidator());
    }
}
