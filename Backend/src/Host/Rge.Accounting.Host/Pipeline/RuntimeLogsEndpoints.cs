using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Rge.BuildingBlocks.Infrastructure.Observability;

namespace Rge.Accounting.Host.Pipeline;

public static class RuntimeLogsEndpoints
{
    public static IEndpointRouteBuilder MapCentralizedRuntimeLogsController(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/diagnostics/runtime-logs")
            .WithTags("Diagnostics.RuntimeLogs")
            .AddEndpointFilter(EnforceDevelopmentOnlyGateAsync);

        // 1. Query recent full-blast transaction logs (filterable by module, handler, correlationId, limit)
        group.MapGet("", (
            string? module,
            string? handler,
            string? correlationId,
            int? limit) =>
        {
            var items = RuntimeExecutionStore.QueryRecent(
                module: module,
                handler: handler,
                correlationId: correlationId,
                limit: limit ?? 50);

            return Results.Ok(new
            {
                environment = RuntimeLogEnvironmentGate.EnvironmentName,
                mode = RuntimeLogEnvironmentGate.Mode.ToString(),
                totalCaptured = items.Count,
                transactions = items.Select(MapEnvelopeDto)
            });
        });

        // 2. Drill down into a single transaction by CorrelationId
        group.MapGet("/{correlationId}", (string correlationId) =>
        {
            var item = RuntimeExecutionStore.GetByCorrelationId(correlationId);
            if (item is null)
            {
                return Results.NotFound(new
                {
                    title = "Runtime Transaction Trace Not Found",
                    status = StatusCodes.Status404NotFound,
                    detail = $"No captured transaction found for CorrelationId '{correlationId}'."
                });
            }

            return Results.Ok(MapEnvelopeDto(item));
        });

        // 3. Clear local in-memory runtime log buffer
        group.MapDelete("", () =>
        {
            RuntimeExecutionStore.Clear();
            return Results.Ok(new
            {
                cleared = true,
                environment = RuntimeLogEnvironmentGate.EnvironmentName,
                timestampUtc = DateTimeOffset.UtcNow
            });
        });

        return app;
    }

    private static async ValueTask<object?> EnforceDevelopmentOnlyGateAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var hostEnv = context.HttpContext.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

        // STRICT PRODUCTION BLOCK:
        // Blocked if ASPNETCORE_ENVIRONMENT == "Production" or if not explicitly "Development"
        if (RuntimeLogEnvironmentGate.IsProductionLocked || hostEnv is null || hostEnv.IsProduction() || !hostEnv.IsDevelopment())
        {
            return Results.Json(
                data: new
                {
                    type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    title = "Runtime Logs Controller Blocked in Production",
                    status = StatusCodes.Status403Forbidden,
                    detail = "Centralized full-blast runtime execution logs are strictly blocked when ASPNETCORE_ENVIRONMENT is 'Production'. Set ASPNETCORE_ENVIRONMENT='Development' locally to enable.",
                    environment = RuntimeLogEnvironmentGate.EnvironmentName
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static object MapEnvelopeDto(RuntimeTransactionEnvelope env) => new
    {
        env.CorrelationId,
        env.TraceId,
        env.SpanId,
        env.Environment,
        env.ExecutionOrigin,
        http = new
        {
            method = env.HttpMethod,
            routeTemplate = env.HttpRouteTemplate,
            rawPath = env.HttpRawPath,
            queryString = env.QueryString,
            statusCode = env.HttpStatusCode,
            actorId = env.ActorId,
            actorRole = env.ActorRole,
            startedAtUtc = env.StartedAtUtc,
            completedAtUtc = env.CompletedAtUtc,
            totalDurationMs = env.TotalDurationMs
        },
        modulesExecuted = env.Handlers.Select(h => h.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        handlers = env.Handlers.ToArray(),
        sqlQueries = env.SqlQueries.ToArray(),
        databaseTransactions = env.DatabaseTransactions.ToArray()
    };
}
