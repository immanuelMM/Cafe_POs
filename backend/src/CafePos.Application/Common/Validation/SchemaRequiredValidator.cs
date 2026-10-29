using FluentValidation;
using FluentValidation.Validators;

namespace CafePos.Application.Common.Validation;

/// <summary>
/// Marks a property as required in the OpenAPI schema without a runtime rule. Used for non-nullable value types (Guid, JsonElement):
/// a missing value binds as the default, so the request has always been accepted and rejected later by business rules, but the
/// published contract lists the property as required.
/// </summary>
public sealed class SchemaRequiredValidator<T, TProperty> : PropertyValidator<T, TProperty>, ISchemaRequired
{
    public override string Name => "SchemaRequiredValidator";

    public override bool IsValid(ValidationContext<T> context, TProperty value) => true;

    protected override string GetDefaultMessageTemplate(string errorCode) => string.Empty;
}
