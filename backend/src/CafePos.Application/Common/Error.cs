namespace CafePos.Application.Common;

/// <summary>
/// An expected, client-facing failure (never an exception). Title/Detail/Extensions are rendered as RFC 9457 ProblemDetails
/// by the Api layer. A null Detail is rendered as the Title (the behavior of the original ApiException).
/// </summary>
public sealed record Error(
    ErrorKind Kind,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, object?>? Extensions = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public int StatusCode => Kind switch
    {
        ErrorKind.BadRequest or ErrorKind.Validation => 400,
        ErrorKind.Unauthorized => 401,
        ErrorKind.Forbidden => 403,
        ErrorKind.NotFound => 404,
        ErrorKind.Conflict => 409,
        ErrorKind.Unprocessable => 422,
        ErrorKind.Locked => 423,
        _ => 500
    };

    public static Error NotFound(string what) => new(ErrorKind.NotFound, "Not found", $"{what} was not found.");

    public static Error Conflict(string title, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(ErrorKind.Conflict, title, detail, extensions);

    public static Error BadRequest(string title, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(ErrorKind.BadRequest, title, detail, extensions);

    public static Error Forbidden(string detail) => new(ErrorKind.Forbidden, "Forbidden", detail);

    public static Error Unauthorized(string title = "Unauthorized", string? detail = null) => new(ErrorKind.Unauthorized, title, detail);

    public static Error Locked(string title, string? detail = null) => new(ErrorKind.Locked, title, detail);

    public static Error Unprocessable(string title, string? detail = null) => new(ErrorKind.Unprocessable, title, detail);

    /// <summary>Field errors keyed by property name; rendered as ValidationProblemDetails.</summary>
    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ErrorKind.Validation, "One or more validation errors occurred.", null, null, fieldErrors);

    public static Error LocationRequired() => new(ErrorKind.BadRequest, "Location required",
        "This request needs an active location. Owners: call POST /api/v1/auth/switch-location or pass locationId.");
}
