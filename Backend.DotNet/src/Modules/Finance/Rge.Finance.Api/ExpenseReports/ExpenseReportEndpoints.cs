using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.ExpenseReports.AddLines;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.ExpenseReports.Evaluate;
using Rge.Finance.Application.ExpenseReports.GetById;
using Rge.Finance.Application.ExpenseReports.List;
using Rge.Finance.Application.ExpenseReports.Patch;
using Rge.Finance.Application.ExpenseReports.Review;
using Rge.Finance.Application.ExpenseReports.Submit;

namespace Rge.Finance.Api.ExpenseReports;

/// <summary>
/// Minimal API endpoints for Monthly Expense Reports & Liquidation (FIN-023 through FIN-030).
/// </summary>
public sealed class ExpenseReportEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapVersionedApiGroup("/api/v1/finance/expense-reports", "ExpenseReports");

        // FIN-023: GET /api/v1/finance/expense-reports
        group.MapGet("/", async (
            string? employee_id,
            string? status,
            int? limit,
            HttpContext httpContext,
            ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListExpenseReportsQuery(employee_id, status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-024: POST /api/v1/finance/expense-reports
        group.MapPost("/", async (
            CreateExpenseReportApiRequest request,
            HttpContext httpContext,
            CreateHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new CreateExpenseReportCommand(
                EmployeeId: request.EmployeeId,
                DepartmentId: request.DepartmentId,
                CostCenterId: request.CostCenterId,
                ReportingPeriod: request.ReportingPeriod,
                BusinessPurposeSummary: request.BusinessPurposeSummary,
                OutOfBaseActivity: request.OutOfBaseActivity,
                ConsolidatedReceiptDocId: request.ConsolidatedReceiptDocId,
                MfarDocId: request.MfarDocId,
                EmployeeDeclarationConfirmed: request.EmployeeDeclarationConfirmed,
                Remarks: request.Remarks,
                Currency: request.Currency ?? "PHP",
                IdempotencyKey: request.IdempotencyKey);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-025: GET /api/v1/finance/expense-reports/{report_id}
        group.MapGet("/{report_id}", async (
            string report_id,
            HttpContext httpContext,
            GetByIdHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetExpenseReportByIdQuery(report_id);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-026: PATCH /api/v1/finance/expense-reports/{report_id}
        group.MapPatch("/{report_id}", async (
            string report_id,
            PatchExpenseReportApiRequest request,
            HttpContext httpContext,
            PatchHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new PatchExpenseReportCommand(
                ReportId: report_id,
                BusinessPurposeSummary: request.BusinessPurposeSummary,
                Remarks: request.Remarks,
                ConsolidatedReceiptDocId: request.ConsolidatedReceiptDocId,
                MfarDocId: request.MfarDocId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-027: POST /api/v1/finance/expense-reports/{report_id}/evaluate
        group.MapPost("/{report_id}/evaluate", async (
            string report_id,
            HttpContext httpContext,
            EvaluateHandler handler,
            CancellationToken ct) =>
        {
            var query = new EvaluateExpenseReportQuery(report_id);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-028: POST /api/v1/finance/expense-reports/{report_id}/lines
        group.MapPost("/{report_id}/lines", async (
            string report_id,
            AddExpenseReportLinesApiRequest request,
            HttpContext httpContext,
            AddLinesHandler handler,
            CancellationToken ct) =>
        {
            var items = request.Lines.Select(l => new AddExpenseReportLineItem(
                TransactionDate: l.TransactionDate,
                ExpenseCategory: l.ExpenseCategory,
                BusinessPurpose: l.BusinessPurpose,
                MerchantOrPayee: l.MerchantOrPayee,
                ClaimedAmountMinor: l.ClaimedAmountMinor,
                SchoolOrDestination: l.SchoolOrDestination,
                ReceiptOrInvoiceNo: l.ReceiptOrInvoiceNo,
                OutOfBase: l.OutOfBase,
                ReceiptDocumentId: l.ReceiptDocumentId,
                MfarDocumentId: l.MfarDocumentId,
                TaxCode: l.TaxCode,
                VatAmountMinor: l.VatAmountMinor)).ToList();

            var cmd = new AddExpenseReportLinesCommand(report_id, items);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-029: POST /api/v1/finance/expense-reports/{report_id}/review
        group.MapPost("/{report_id}/review", async (
            string report_id,
            ReviewExpenseReportApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            ReviewHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ReviewExpenseReportCommand(
                ReportId: report_id,
                ReviewerId: actor.ActorId,
                Decision: request.Outcome,
                Notes: request.Notes,
                RejectionReason: request.RejectionReason);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-030: POST /api/v1/finance/expense-reports/{report_id}/submit
        group.MapPost("/{report_id}/submit", async (
            string report_id,
            HttpContext httpContext,
            ICurrentActor actor,
            SubmitHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SubmitExpenseReportCommand(
                ReportId: report_id,
                ActorId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
