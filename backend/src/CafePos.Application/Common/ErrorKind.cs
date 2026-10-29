namespace CafePos.Application.Common;

/// <summary>Category of an expected failure; the Api layer maps each kind to an HTTP status code.</summary>
public enum ErrorKind
{
    BadRequest,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Unprocessable,
    Locked
}
