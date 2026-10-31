using Microsoft.AspNetCore.Routing;



namespace Rge.BuildingBlocks.Api.Endpoints;



/// <summary>

/// Contract implemented by each module's API project to register Minimal API route groups.

/// </summary>

public interface IEndpointModule

{

    void MapEndpoints(IEndpointRouteBuilder app);

}
