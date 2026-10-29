using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Users;

internal sealed class AssignmentRequestValidator : RequestValidator<AssignmentRequest>
{
    public AssignmentRequestValidator()
    {
        RuleFor(x => x.LocationId).SchemaRequired();
        RuleFor(x => x.Role).IsRequired();
    }
}

internal sealed class CreateUserRequestValidator : RequestValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).IsRequired().Email().MaxLen(256);
        RuleFor(x => x.DisplayName).IsRequired().MaxLen(200);
        RuleFor(x => x.Password).IsRequired().MinLen(8).MaxLen(100);
        RuleFor(x => x.Assignments).IsPresent();
        RuleForEach(x => x.Assignments).SetValidator(new AssignmentRequestValidator());
    }
}

internal sealed class UpdateUserRequestValidator : RequestValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).IsRequired().MaxLen(200);
    }
}

internal sealed class SetPinRequestValidator : RequestValidator<SetPinRequest>
{
    public SetPinRequestValidator()
    {
        RuleFor(x => x.Pin).IsRequired().Pattern("^[0-9]{4,6}$", "PIN must be 4 to 6 digits.");
    }
}
