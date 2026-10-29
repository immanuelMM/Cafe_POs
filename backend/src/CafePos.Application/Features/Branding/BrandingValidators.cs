using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Branding;

internal sealed class UpdateBrandingRequestValidator : RequestValidator<UpdateBrandingRequest>
{
    public UpdateBrandingRequestValidator()
    {
        RuleFor(x => x.BrandName).IsRequired().MaxLen(100);
        RuleFor(x => x.Tagline).MaxLen(200);
        RuleFor(x => x.PrimaryColor).IsRequired().Pattern("^#[0-9a-fA-F]{6}$", "Primary color must look like #RRGGBB.");
        RuleFor(x => x.SecondaryColor).Pattern("^(#[0-9a-fA-F]{6})?$", "Secondary color must look like #RRGGBB.");
        RuleFor(x => x.TertiaryColor).Pattern("^(#[0-9a-fA-F]{6})?$", "Tertiary color must look like #RRGGBB.");
        RuleFor(x => x.GradientAngle).Between(0, 360);
    }
}

internal sealed class SaveBrandDesignRequestValidator : RequestValidator<SaveBrandDesignRequest>
{
    public SaveBrandDesignRequestValidator()
    {
        RuleFor(x => x.Document).SchemaRequired();
    }
}
