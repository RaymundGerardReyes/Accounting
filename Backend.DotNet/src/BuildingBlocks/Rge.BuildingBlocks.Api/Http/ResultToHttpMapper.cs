using Microsoft.AspNetCore.Http;

using Rge.BuildingBlocks.Api.Serialization;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Api.Http;



/// <summary>

/// Maps domain Result and Result&lt;T&gt; instances directly to Minimal API IResult responses.

/// </summary>

public static class ResultToHttpMapper

{

    public static IResult ToHttpResult(this Result result, HttpContext httpContext)

    {

        ArgumentNullException.ThrowIfNull(result);

        ArgumentNullException.ThrowIfNull(httpContext);



        if (result.IsSuccess)

        {

            return Results.Ok(new Dictionary<string, object>

            {

                ["status"] = "ok",

                ["correlation_id"] = httpContext.TraceIdentifier

            });

        }



        return ToProblemResult(result.Error, httpContext);

    }



    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext, int successStatusCode = StatusCodes.Status200OK)

    {

        ArgumentNullException.ThrowIfNull(result);

        ArgumentNullException.ThrowIfNull(httpContext);



        if (result.IsSuccess)

        {

            return Results.Json(result.Value, JsonDefaults.Options, statusCode: successStatusCode);

        }



        return ToProblemResult(result.Error, httpContext);

    }

    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext, Func<T, IResult> onSuccess)

    {

        ArgumentNullException.ThrowIfNull(result);

        ArgumentNullException.ThrowIfNull(httpContext);

        ArgumentNullException.ThrowIfNull(onSuccess);



        if (result.IsSuccess)

        {

            return onSuccess(result.Value);

        }



        return ToProblemResult(result.Error, httpContext);

    }



    private static IResult ToProblemResult(Error error, HttpContext httpContext)

    {

        var problem = ProblemDetailsFactory.Create(httpContext, error);

        return Results.Json(

            problem,

            AppJsonContext.Default.ProblemDetails,

            contentType: "application/problem+json",

            statusCode: problem.Status ?? StatusCodes.Status500InternalServerError);

    }

}
