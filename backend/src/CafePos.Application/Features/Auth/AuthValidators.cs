using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.Auth;

internal sealed class LoginRequestValidator : RequestValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).IsRequired().Email();
        RuleFor(x => x.Password).IsRequired();
    }
}
