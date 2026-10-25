using Microsoft.AspNetCore.Http;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Api.Http;

/// <summary>
/// Maps domain/application Result objects to ASP.NET Core Minimal API IResult responses.
/// </summary>
public static class ResultToHttpMapper
{
    public static IResult ToHttpResult(this Result result, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (result.IsSuccess)
        {
            return Results.NoContent();
        }

        var problem = ProblemDetailsFactory.Create(httpContext, result.Error);
        return Results.Problem(problem);
    }

    public static IResult ToHttpResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        Func<T, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (result.IsSuccess)
        {
            return onSuccess is not null
                ? onSuccess(result.Value)
                : Results.Ok(result.Value);
        }

        var problem = ProblemDetailsFactory.Create(httpContext, result.Error);
        return Results.Problem(problem);
    }
}
