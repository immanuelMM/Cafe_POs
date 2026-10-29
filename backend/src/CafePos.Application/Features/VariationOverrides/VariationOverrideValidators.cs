using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.VariationOverrides;

internal sealed class UpsertLocationOverrideRequestValidator : RequestValidator<UpsertLocationOverrideRequest>
{
    public UpsertLocationOverrideRequestValidator()
    {
        RuleFor(x => x.PriceOverride).Between(0m, 1_000_000m);
        RuleFor(x => x.LowStockThresholdOverride).Between(0m, 1_000_000_000m);
    }
}
