using CafePos.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Http;

/// <summary>Maps application results to HTTP: success to 200/201/204, failure to a problem response.</summary>
public static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : new ProblemResult(result.Error!);

    /// <summary>201 with a Location header built from the created value.</summary>
    public static ActionResult<T> ToCreated<T>(this Result<T> result, Func<T, string> location)
    {
        if (result.IsFailure) return new ProblemResult(result.Error!);
        return new CreatedResult(location(result.Value), result.Value);
    }

    /// <summary>204 on success.</summary>
    public static IActionResult ToNoContent(this Result result) =>
        result.IsSuccess ? new NoContentResult() : new ProblemResult(result.Error!);

    public static IActionResult ToProblem(this Error error) => new ProblemResult(error);
}
