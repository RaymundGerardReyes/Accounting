using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Finance.Application.Reporting.BusinessConfirmations;
using Rge.Finance.Application.Reporting.DashboardSummary;
using Rge.Finance.Application.Reporting.KpiDefinitions;
using Rge.Finance.Application.Reporting.Tracker;
using Rge.Finance.Application.Reporting.TrackerCsvExport;
using Rge.Finance.Domain.Approvals;

namespace Rge.Finance.Api.Reporting;

/// <summary>
/// Minimal API endpoints for Finance Operations Reporting, Auditing & KPI Analytics (FIN-001, FIN-002, FIN-012, FIN-031, FIN-052, FIN-053).
/// </summary>
public sealed class ReportingEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // FIN-001: GET /api/v1/finance/approval-matrix
        app.MapGet("/api/v1/finance/approval-matrix", () =>
        {
            IReadOnlyList<ApprovalMatrix> defaultMatrix = ApprovalMatrix.CreateDefaultConfiguration();
            return Results.Ok(defaultMatrix);
        }).WithTags("Reporting");

        // FIN-002: GET /api/v1/finance/business-confirmations
        app.MapGet("/api/v1/finance/business-confirmations", async (
            HttpContext httpContext,
            BusinessConfirmationsHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetBusinessConfirmationsQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Reporting");

        // FIN-012: GET /api/v1/finance/dashboard/summary
        app.MapGet("/api/v1/finance/dashboard/summary", async (
            string? start_date,
            string? end_date,
            string? as_of_date,
            string? department_id,
            HttpContext httpContext,
            DashboardSummaryHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetDashboardSummaryQuery(start_date, end_date, as_of_date, department_id);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Reporting");

        // FIN-031: GET /api/v1/finance/kpi-definitions
        app.MapGet("/api/v1/finance/kpi-definitions", async (
            HttpContext httpContext,
            KpiDefinitionsHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetKpiDefinitionsQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Reporting");

        // FIN-052: GET /api/v1/finance/tracker
        app.MapGet("/api/v1/finance/tracker", async (
            string? workflow,
            string? department_id,
            string? status,
            string? search,
            int? limit,
            HttpContext httpContext,
            TrackerHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetCentralizedTrackerQuery(workflow, department_id, status, search, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Reporting");

        // FIN-053: GET /api/v1/finance/tracker/export.csv
        app.MapGet("/api/v1/finance/tracker/export.csv", async (
            string? workflow,
            string? department_id,
            string? status,
            TrackerCsvExportHandler handler,
            CancellationToken ct) =>
        {
            var query = new ExportTrackerCsvQuery(workflow, department_id, status);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message });
            }

            return Results.File(result.Value, "text/csv; charset=utf-8", "finance_tracker_export.csv");
        }).WithTags("Reporting");
    }
}
