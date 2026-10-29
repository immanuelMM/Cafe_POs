using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Inventory;

internal sealed class CreateIngredientRequestValidator : RequestValidator<CreateIngredientRequest>
{
    public CreateIngredientRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Unit).IsRequired().MaxLen(20);
        RuleFor(x => x.LowStockThreshold).Between(0m, 1_000_000_000m);
        RuleFor(x => x.InitialQuantity).Between(0m, 1_000_000_000m);
    }
}

internal sealed class UpdateIngredientRequestValidator : RequestValidator<UpdateIngredientRequest>
{
    public UpdateIngredientRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Unit).IsRequired().MaxLen(20);
        RuleFor(x => x.LowStockThreshold).Between(0m, 1_000_000_000m);
    }
}

internal sealed class RestockRequestValidator : RequestValidator<RestockRequest>
{
    public RestockRequestValidator()
    {
        RuleFor(x => x.IngredientId).SchemaRequired();
        RuleFor(x => x.Quantity).Between(0.001m, 1_000_000_000m);
        RuleFor(x => x.UnitCost).Between(0m, 1_000_000_000m);
        RuleFor(x => x.Supplier).MaxLen(200);
        RuleFor(x => x.Note).MaxLen(500);
    }
}

internal sealed class AdjustmentRequestValidator : RequestValidator<AdjustmentRequest>
{
    public AdjustmentRequestValidator()
    {
        RuleFor(x => x.Reason).IsPresentText();
        RuleFor(x => x.Note).IsRequired().MaxLen(500);
    }
}

internal sealed class SetRecipeLineValidator : RequestValidator<SetRecipeLine>
{
    public SetRecipeLineValidator()
    {
        RuleFor(x => x.IngredientId).SchemaRequired();
        RuleFor(x => x.Quantity).Between(0.001m, 1_000_000m);
    }
}

internal sealed class SetRecipeRequestValidator : RequestValidator<SetRecipeRequest>
{
    public SetRecipeRequestValidator()
    {
        RuleFor(x => x.Lines).IsPresent();
        RuleForEach(x => x.Lines).SetValidator(new SetRecipeLineValidator());
    }
}
