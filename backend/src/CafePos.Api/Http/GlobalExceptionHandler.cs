using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Http;

/// <summary>
/// Last-resort handler for unexpected failures, rendered as RFC 9457 ProblemDetails. Expected failures never reach it: they are
/// returned as Results and rendered by <see cref="ProblemResult"/>. Internal details are never sent to the client.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails pd;
        if (ex is BadHttpRequestException bad && bad.StatusCode is >= 400 and < 500)
        {
            pd = new ProblemDetails { Status = bad.StatusCode, Title = bad.StatusCode == 413 ? "Request too large" : "Bad request", Detail = bad.Message };
        }
        else
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            pd = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
        }

        ctx.Response.StatusCode = pd.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }
}
