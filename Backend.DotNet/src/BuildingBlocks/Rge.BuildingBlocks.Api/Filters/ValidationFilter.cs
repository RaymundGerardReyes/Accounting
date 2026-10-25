using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Api.Filters;

/// <summary>
/// Minimal API endpoint filter that intercepts incoming request arguments and executes
/// validation checks before reaching the route handler. If validation fails, returns an
/// RFC 9457 ProblemDetails with HTTP 400 Bad Request and the correlation ID.
/// </summary>
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        foreach (var argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            var validationContext = new ValidationContext(argument);
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(argument, validationContext, validationResults, validateAllProperties: true))
            {
                var firstError = validationResults.FirstOrDefault()?.ErrorMessage ?? "Validation failed.";
                var memberNames = string.Join(", ", validationResults.SelectMany(r => r.MemberNames));
                var error = Error.Validation(
                    "Request.ValidationError",
                    $"Validation failed for {argument.GetType().Name}: {firstError} ({memberNames})");

                var problem = ProblemDetailsFactory.Create(context.HttpContext, error);
                return Results.Problem(problem);
            }
        }

        return await next(context);
    }
}
