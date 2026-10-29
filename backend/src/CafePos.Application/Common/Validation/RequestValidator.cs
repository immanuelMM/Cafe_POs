using FluentValidation;

namespace CafePos.Application.Common.Validation;

/// <summary>
/// Base class for request validators. Messages keep the wording and field keys the API produced with DataAnnotations
/// ("The Name field is required.", keys like "Variations[0].Name"), so clients see the same errors.
/// </summary>
public abstract class RequestValidator<T> : AbstractValidator<T>
{
    static RequestValidator()
    {
        // {PropertyName} in messages is the C# member name, not the humanized name FluentValidation uses by default.
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => member?.Name;
    }
}
