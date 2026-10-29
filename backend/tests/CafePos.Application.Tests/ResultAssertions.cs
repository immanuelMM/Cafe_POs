using CafePos.Application.Common;

namespace CafePos.Application.Tests;

internal static class ResultAssertions
{
    public static Error ShouldFailWith(this Result result, ErrorKind kind, string? title = null)
    {
        Assert.True(result.IsFailure, "Expected a failed result");
        Assert.Equal(kind, result.Error!.Kind);
        if (title is not null) Assert.Equal(title, result.Error.Title);
        return result.Error;
    }

    public static Error ShouldFailWith(this Error error, ErrorKind kind, string? title = null)
    {
        Assert.Equal(kind, error.Kind);
        if (title is not null) Assert.Equal(title, error.Title);
        return error;
    }

    public static T ShouldSucceed<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error is null ? "" : $"Expected success but got {result.Error.Kind}: {result.Error.Title} ({result.Error.Detail})");
        return result.Value;
    }

    public static void ShouldSucceed(this Result result) =>
        Assert.True(result.IsSuccess, result.Error is null ? "" : $"Expected success but got {result.Error.Kind}: {result.Error.Title} ({result.Error.Detail})");
}
