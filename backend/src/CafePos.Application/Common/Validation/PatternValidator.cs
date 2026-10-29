using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;

namespace CafePos.Application.Common.Validation;

/// <summary>
/// Regular expression rule with [RegularExpression] semantics: null and empty values pass (a Required rule reports them), anything
/// else must match. Implements <see cref="IRegularExpressionValidator"/> so the pattern is published in the OpenAPI schema.
/// </summary>
public sealed class PatternValidator<T>(string expression) : PropertyValidator<T, string?>, IRegularExpressionValidator
{
    private readonly Regex _regex = new(expression, RegexOptions.None, TimeSpan.FromSeconds(1));

    public override string Name => "PatternValidator";

    public string Expression => expression;

    public override bool IsValid(ValidationContext<T> context, string? value) =>
        string.IsNullOrEmpty(value) || _regex.IsMatch(value);

    protected override string GetDefaultMessageTemplate(string errorCode) => "'{PropertyName}' is not in the correct format.";
}
