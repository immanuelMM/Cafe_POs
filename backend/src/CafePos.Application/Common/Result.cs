namespace CafePos.Application.Common;

/// <summary>Outcome of a use case without a value. Expected failures are returned, not thrown.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);
    public static Result<T> Success<T>(T value) => new(value);
    public static Result<T> Failure<T>(Error error) => new(error);

    public static implicit operator Result(Error error) => new(error);
}
