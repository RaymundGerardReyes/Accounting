using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.CostAllocation.Application.GetCostAllocation;
using Rge.CostAllocation.Application.Ports;
using Rge.CostAllocation.Application.UpdateDepartmentCost;
using Rge.CostAllocation.Infrastructure;

namespace Rge.CostAllocation.Api;

public sealed class CostAllocationEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Endpoint #38: GET /api/cost-allocation
        app.MapGet("/api/cost-allocation", async (
            HttpContext httpContext,
            GetCostAllocationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCostAllocationQuery(), ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("CostAllocation");

        // Endpoint #39: POST /api/cost-allocation/department
        app.MapPost("/api/cost-allocation/department", async (
            UpdateDepartmentCostCommand command,
            HttpContext httpContext,
            UpdateDepartmentCostHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("CostAllocation");

        // Endpoints #2-#5: Legacy Dashboard, Department, Products, Variance
        app.MapGet("/api/dashboard", (LegacyDashboardReader reader) =>
            Results.Json(reader.BuildDashboardSummary(), JsonDefaults.Options)).WithTags("CostAllocation.Legacy");

        app.MapGet("/api/department/{name}", (string name, HttpContext httpContext, ICostAllocationStore store) =>
        {
            CostDepartmentDto? dept = store.GetDepartmentByName(name);
            return dept is not null
                ? Results.Json(dept, JsonDefaults.Options)
                : Result.Failure(Error.NotFound("CostAllocation.NotFound", $"Department '{name}' not found.")).ToHttpResult(httpContext);
        }).WithTags("CostAllocation.Legacy");

        app.MapGet("/api/products", (ICostAllocationStore store) =>
            Results.Json(store.GetSnapshot().Products, JsonDefaults.Options)).WithTags("CostAllocation.Legacy");

        app.MapGet("/api/variance", (LegacyDashboardReader reader) =>
            Results.Json(reader.BuildVarianceAnalysis(), JsonDefaults.Options)).WithTags("CostAllocation.Legacy");

        // Endpoints #6-#9: Configuration endpoints
        app.MapGet("/api/configuration", (ICostAllocationStore store) =>
            Results.Json(store.GetConfiguration(), JsonDefaults.Options)).WithTags("CostAllocation.Configuration");

        app.MapPut("/api/configuration", (Dictionary<string, object> config, ICostAllocationStore store) =>
        {
            store.SaveConfiguration(config);
            return Results.Json(store.GetConfiguration(), JsonDefaults.Options);
        }).WithTags("CostAllocation.Configuration");
    }
}

