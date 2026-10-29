using System.Globalization;
using FluentValidation;

namespace CafePos.Application.Common.Validation;

/// <summary>Rule helpers that reproduce the wording of the DataAnnotations attributes the API used before FluentValidation.</summary>
public static class ValidationRules
{
    /// <summary>[Required] on a string: fails for null, empty and whitespace.</summary>
    public static IRuleBuilderOptions<T, string?> IsRequired<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().WithMessage("The {PropertyName} field is required.");

    /// <summary>[Required] on a nullable value type or reference: fails for null only.</summary>
    public static IRuleBuilderOptions<T, TProperty?> IsRequired<T, TProperty>(this IRuleBuilder<T, TProperty?> rule) where TProperty : struct =>
        rule.NotNull().WithMessage("The {PropertyName} field is required.");

    /// <summary>
    /// Runtime-only "required" for non-nullable reference properties that have a default value (MVC adds an implicit Required for
    /// them). Not part of the published schema, so it is a plain predicate rather than NotNull/NotEmpty.
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> IsPresent<T, TProperty>(this IRuleBuilder<T, TProperty> rule) where TProperty : class =>
        rule.Must(v => v is not null).WithMessage("The {PropertyName} field is required.");

    /// <summary>Runtime-only "required" for a string with a default value: null, empty and whitespace fail, no schema change.</summary>
    public static IRuleBuilderOptions<T, string> IsPresentText<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("The {PropertyName} field is required.");

    /// <summary>Lists the property as required in the OpenAPI schema only (see <see cref="SchemaRequiredValidator{T, TProperty}"/>).</summary>
    public static IRuleBuilderOptions<T, TProperty> SchemaRequired<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.SetValidator(new SchemaRequiredValidator<T, TProperty>());

    public static IRuleBuilderOptions<T, string?> MaxLen<T>(this IRuleBuilder<T, string?> rule, int max) =>
        rule.MaximumLength(max)
            .WithMessage($"The field {{PropertyName}} must be a string or array type with a maximum length of '{max}'.");

    public static IRuleBuilderOptions<T, string?> MinLen<T>(this IRuleBuilder<T, string?> rule, int min) =>
        rule.MinimumLength(min)
            .WithMessage($"The field {{PropertyName}} must be a string or array type with a minimum length of '{min}'.");

    /// <summary>[EmailAddress]: null passes (Required reports it); otherwise a single '@' that is neither first nor last.</summary>
    public static IRuleBuilderOptions<T, string?> Email<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(IsEmail).WithMessage("The {PropertyName} field is not a valid e-mail address.");

    public static IRuleBuilderOptions<T, string?> Pattern<T>(this IRuleBuilder<T, string?> rule, string regex, string message) =>
        rule.SetValidator(new PatternValidator<T>(regex)).WithMessage(message);

    public static IRuleBuilderOptions<T, decimal> Between<T>(this IRuleBuilder<T, decimal> rule, decimal from, decimal to) =>
        rule.InclusiveBetween(from, to).WithMessage(RangeMessage(from, to));

    public static IRuleBuilderOptions<T, decimal?> Between<T>(this IRuleBuilder<T, decimal?> rule, decimal from, decimal to) =>
        rule.InclusiveBetween(from, to).WithMessage(RangeMessage(from, to));

    public static IRuleBuilderOptions<T, int> Between<T>(this IRuleBuilder<T, int> rule, int from, int to) =>
        rule.InclusiveBetween(from, to).WithMessage(RangeMessage(from, to));

    private static string RangeMessage(decimal from, decimal to) =>
        $"The field {{PropertyName}} must be between {from.ToString(CultureInfo.InvariantCulture)} and {to.ToString(CultureInfo.InvariantCulture)}.";

    private static bool IsEmail(string? value)
    {
        if (value is null) return true;
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at != value.Length - 1 && at == value.LastIndexOf('@');
    }
}
