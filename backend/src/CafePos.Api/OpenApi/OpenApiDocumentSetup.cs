using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CafePos.Api.OpenApi;

/// <summary>Document title/version and the Bearer security scheme (same as the former Swashbuckle setup).</summary>
internal sealed class OpenApiDocumentSetup : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo { Title = "Cafe POS API", Version = "v1" };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the accessToken returned by POST /api/v1/auth/login."
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }
        ];
        return Task.CompletedTask;
    }
}

/// <summary>
/// The built-in generator describes every number as <c>number | string</c> (with a pattern) because the web JSON defaults
/// allow reading numbers from strings. Responses always carry real numbers and the frontend only sends numbers, so the
/// string alternative would only make generated TypeScript types wrong (<c>number | string</c>). Keep just the numeric type.
/// </summary>
internal sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is { } t && t.HasFlag(JsonSchemaType.String)
            && (t.HasFlag(JsonSchemaType.Integer) || t.HasFlag(JsonSchemaType.Number)))
        {
            schema.Type = t & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// File-upload endpoints bind <c>[FromForm] IFormFile</c> and accept multipart/form-data; the generator labels the body
/// application/x-www-form-urlencoded. Relabel it (documentation only, no runtime [Consumes] change).
/// </summary>
internal sealed class MultipartUploadOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var content = operation.RequestBody?.Content;
        var hasFile = context.Description.ParameterDescriptions.Any(p =>
            p.Type == typeof(IFormFile) || p.Type?.GetProperties().Any(x => x.PropertyType == typeof(IFormFile)) == true);
        if (hasFile && content is not null && content.Remove("application/x-www-form-urlencoded", out var media))
            content["multipart/form-data"] = media;
        return Task.CompletedTask;
    }
}
