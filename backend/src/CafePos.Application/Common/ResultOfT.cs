namespace CafePos.Application.Common;

/// <summary>Outcome of a use case that returns a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(null) => _value = value;

    internal Result(Error error) : base(error) { }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value: " + Error!.Title);

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map) => IsSuccess ? new Result<TOut>(map(Value)) : new Result<TOut>(Error!);
}
