using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Pagination;
using Rge.Payments.Application.Orders.ListOrders;

namespace Rge.Payments.Api.Orders;

public sealed class ListOrdersEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/orders", HandleLegacyAsync)
           .WithName("ListOrders")
           .WithTags("Payments.Orders");

        app.MapGet("/api/orders", HandlePagedAsync)
           .WithName("ListOrdersPaged")
           .WithTags("Orders");
    }

    private static async Task<IResult> HandleLegacyAsync(
        HttpContext httpContext,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        ListOrdersHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        if (page.HasValue || pageSize.HasValue || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(category))
        {
            return await HandlePagedAsync(httpContext, handler, page, pageSize ?? limit, status, category, search, sortBy, sortDir, cancellationToken).ConfigureAwait(false);
        }

        var query = new ListOrdersQuery(status, limit ?? 50);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, orders => Results.Ok(orders));
    }

    private static async Task<IResult> HandlePagedAsync(
        HttpContext httpContext,
        [FromServices] ListOrdersHandler handler,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var handlerSw = Stopwatch.StartNew();

        var pageRequest = new PageRequest(
            page: page,
            pageSize: pageSize,
            status: status,
            category: category,
            search: search,
            sortBy: sortBy,
            sortDir: sortDir);

        var result = await handler.HandlePagedAsync(
            new ListOrdersQuery(pageRequest),
            cancellationToken).ConfigureAwait(false);

        handlerSw.Stop();

        if (result.IsSuccess && result.Value.Timing is { } timing)
        {
            var totalHandlerMs = handlerSw.Elapsed.TotalMilliseconds;
            var serverTimingHeader = string.Create(
                CultureInfo.InvariantCulture,
                $"db-conn;dur={timing.ConnectionAcquireMs:F2}, sql;dur={timing.SqlDurationMs:F2}, materialize;dur={timing.MaterializeDurationMs:F2}, handler;dur={totalHandlerMs:F2}");

            httpContext.Response.Headers.Append("Server-Timing", serverTimingHeader);
        }

        return result.ToHttpResult(httpContext, response => Results.Ok(response));
    }
}
