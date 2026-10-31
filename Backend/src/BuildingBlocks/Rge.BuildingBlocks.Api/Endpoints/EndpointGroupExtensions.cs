using Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Routing;



namespace Rge.BuildingBlocks.Api.Endpoints;



/// <summary>

/// Helper extensions for organizing Minimal API route groups with consistent tags and ProblemDetails metadata.

/// </summary>

public static class EndpointGroupExtensions

{

    public static RouteGroupBuilder MapVersionedApiGroup(

        this IEndpointRouteBuilder app,

        string prefix,

        string tag)

    {

        ArgumentNullException.ThrowIfNull(app);

        return app

            .MapGroup(prefix)

            .WithTags(tag)

            .ProducesProblem(StatusCodes.Status400BadRequest)

            .ProducesProblem(StatusCodes.Status401Unauthorized)

            .ProducesProblem(StatusCodes.Status403Forbidden)

            .ProducesProblem(StatusCodes.Status500InternalServerError);

    }

}
