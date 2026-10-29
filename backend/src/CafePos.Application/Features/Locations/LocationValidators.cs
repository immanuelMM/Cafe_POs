using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Locations;

internal sealed class CreateLocationRequestValidator : RequestValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Address).MaxLen(500);
        RuleFor(x => x.TimeZone).IsRequired().MaxLen(100);
        RuleFor(x => x.ContactPhone).MaxLen(50);
        RuleFor(x => x.VatRatePercent).Between(0m, 100m);
        RuleFor(x => x.StripeTerminalLocationId).MaxLen(100);
    }
}

internal sealed class UpdateLocationRequestValidator : RequestValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.Name).IsRequired().MaxLen(200);
        RuleFor(x => x.Address).MaxLen(500);
        RuleFor(x => x.TimeZone).IsRequired().MaxLen(100);
        RuleFor(x => x.ContactPhone).MaxLen(50);
        RuleFor(x => x.VatRatePercent).Between(0m, 100m);
        RuleFor(x => x.StripeTerminalLocationId).MaxLen(100);
    }
}

internal sealed class AssignStaffByEmailRequestValidator : RequestValidator<AssignStaffByEmailRequest>
{
    public AssignStaffByEmailRequestValidator()
    {
        RuleFor(x => x.Email).IsRequired().Email().MaxLen(256);
        RuleFor(x => x.Role).IsRequired();
    }
}
