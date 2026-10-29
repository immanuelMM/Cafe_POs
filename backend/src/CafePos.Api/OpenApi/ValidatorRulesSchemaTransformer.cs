using System.Globalization;
using CafePos.Application.Common.Validation;
using FluentValidation;
using FluentValidation.Validators;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CafePos.Api.OpenApi;

/// <summary>
/// Publishes the constraints of the FluentValidation validators in the OpenAPI schema (required, maxLength, minLength,
/// minimum/maximum, pattern), so the contract the frontend generates its types from keeps describing what the API enforces.
/// The validators are the single source of truth; nothing is declared twice.
/// </summary>
internal sealed class ValidatorRulesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonPropertyInfo is not null || schema.Properties is null || schema.Properties.Count == 0)
            return Task.CompletedTask;

        var validatorType = typeof(IValidator<>).MakeGenericType(context.JsonTypeInfo.Type);
        if (context.ApplicationServices.GetService(validatorType) is not IValidator validator)
            return Task.CompletedTask;

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in validator.CreateDescriptor().GetMembersWithValidators())
        {
            var key = schema.Properties.Keys.FirstOrDefault(k => string.Equals(k, member.Key, StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var property = schema.Properties[key] as OpenApiSchema;

            foreach (var (rule, _) in member)
            {
                switch (rule)
                {
                    case INotNullValidator or INotEmptyValidator or ISchemaRequired:
                        required.Add(key);
                        break;
                    case ILengthValidator length when property is not null:
                        if (length.Max is > 0 and < int.MaxValue) property.MaxLength = length.Max;
                        if (length.Min > 0) property.MinLength = length.Min;
                        break;
                    case IBetweenValidator between when property is not null:
                        property.Minimum = Format(between.From);
                        property.Maximum = Format(between.To);
                        break;
                    case IRegularExpressionValidator pattern when property is not null:
                        property.Pattern = pattern.Expression;
                        break;
                }
            }
        }

        if (required.Count > 0)
        {
            schema.Required ??= new HashSet<string>();
            foreach (var key in schema.Properties.Keys.Where(required.Contains)) // declaration order of the properties
                schema.Required.Add(key);
        }
        return Task.CompletedTask;
    }

    private static string Format(object value) => Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
