using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CafePos.Api.Validation;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument (request DTOs bound from the body) before the action.
/// Failures use the same 400 ValidationProblemDetails response as MVC model validation (field keys such as "Variations[0].Name"),
/// so clients keep seeing <c>errors: { field: [messages] }</c>.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services, IOptions<ApiBehaviorOptions> behavior) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var error in result.Errors)
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = behavior.Value.InvalidModelStateResponseFactory(context);
            return;
        }
        await next();
    }
}
