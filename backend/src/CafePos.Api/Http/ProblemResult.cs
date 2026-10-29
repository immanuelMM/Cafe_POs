using CafePos.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CafePos.Api.Http;

/// <summary>
/// Renders an application <see cref="Error"/> as an RFC 9457 problem response: status, title, detail (the title when there is
/// none) and extension members as before the Result refactor; field errors become a ValidationProblemDetails.
/// </summary>
public sealed class ProblemResult(Error error) : ActionResult
{
    public Error Error { get; } = error;

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;

        if (Error.Kind == ErrorKind.Validation && Error.FieldErrors is not null)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in Error.FieldErrors)
                foreach (var message in messages)
                    modelState.AddModelError(field, message);
            var factory = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            var validation = factory.CreateValidationProblemDetails(http, modelState);
            await new BadRequestObjectResult(validation).ExecuteResultAsync(context);
            return;
        }

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<ProblemResult>();
        logger.LogInformation("Request rejected: {Status} {Title} ({Path})", Error.StatusCode, Error.Title, http.Request.Path);

        var problem = new ProblemDetails { Status = Error.StatusCode, Title = Error.Title, Detail = Error.Detail ?? Error.Title };
        if (Error.Extensions is not null)
            foreach (var (key, value) in Error.Extensions) problem.Extensions[key] = value;

        http.Response.StatusCode = Error.StatusCode;
        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}
