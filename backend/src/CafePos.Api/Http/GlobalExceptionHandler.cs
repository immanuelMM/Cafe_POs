using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Http;

/// <summary>
/// Last-resort handler for unexpected failures, rendered as RFC 9457 ProblemDetails. Expected failures never reach it: they are
/// returned as Results and rendered by <see cref="ProblemResult"/>. Internal details are never sent to the client.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails pd;
        if (exception is BadHttpRequestException bad && bad.StatusCode is >= 400 and < 500)
        {
            pd = new ProblemDetails { Status = bad.StatusCode, Title = bad.StatusCode == 413 ? "Request too large" : "Bad request", Detail = bad.Message };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            pd = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
        }

        httpContext.Response.StatusCode = pd.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = pd, Exception = exception });
    }
}
