using Microsoft.AspNetCore.Routing;

namespace Rge.BuildingBlocks.Api.Endpoints;

/// <summary>
/// Implemented by each module's Api layer to mount its Minimal API endpoints into the Host pipeline.
/// </summary>
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder app);
}
