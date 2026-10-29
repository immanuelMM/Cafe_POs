using CafePos.Application.Common.Validation;
using FluentValidation;

namespace CafePos.Application.Features.TimeClock;

internal sealed class PunchRequestValidator : RequestValidator<PunchRequest>
{
    public PunchRequestValidator()
    {
        RuleFor(x => x.UserId).IsRequired();
        RuleFor(x => x.Pin).IsRequired();
    }
}
