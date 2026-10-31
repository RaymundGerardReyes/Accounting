using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.CashAdvances.CashReturn;
using Rge.Finance.Application.CashAdvances.Close;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.CashAdvances.Decide;
using Rge.Finance.Application.CashAdvances.GetById;
using Rge.Finance.Application.CashAdvances.List;
using Rge.Finance.Application.CashAdvances.ReimbursementSettle;
using Rge.Finance.Application.CashAdvances.Submit;
using Rge.Finance.Application.Disbursements.Prepare;

namespace Rge.Finance.Api.CashAdvances;

/// <summary>
/// Minimal API endpoints for Cash Advance management (FIN-003 through FIN-011).
/// </summary>
public sealed class CashAdvanceEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapVersionedApiGroup("/api/v1/finance/cash-advances", "CashAdvances");

        // FIN-003: GET /api/v1/finance/cash-advances
        group.MapGet("/", async (
            string? employee_id,
            string? status,
            int? limit,
            HttpContext httpContext,
            ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListCashAdvancesQuery(employee_id, status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-004: POST /api/v1/finance/cash-advances
        group.MapPost("/", async (
            CreateCashAdvanceApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            CreateCashAdvanceHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new CreateCashAdvanceCommand(
                RequestorId: actor.ActorId,
                EmployeeId: request.EmployeeId,
                DepartmentId: request.DepartmentId,
                CostCenterId: request.CostCenterId,
                PurposeCode: request.PurposeCategory,
                BusinessJustification: request.BusinessPurpose,
                RequestedAmountMinor: request.RequestedAmountMinor,
                DateNeeded: request.RequestedDisbursementDate,
                Currency: request.Currency ?? "PHP",
                DestinationOrSite: null,
                ActivityStartDate: null,
                ActivityEndDate: null,
                Remarks: request.Remarks,
                AttachmentIds: null,
                IdempotencyKey: request.IdempotencyKey);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-005: GET /api/v1/finance/cash-advances/{advance_id}
        group.MapGet("/{advance_id}", async (
            string advance_id,
            HttpContext httpContext,
            GetByIdHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetCashAdvanceByIdQuery(advance_id);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-006: POST /api/v1/finance/cash-advances/{advance_id}/cash-return
        group.MapPost("/{advance_id}/cash-return", async (
            string advance_id,
            CashReturnApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            CashReturnHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new RecordCashReturnCommand(
                AdvanceId: advance_id,
                ActorId: actor.ActorId,
                ReturnedAmountMinor: request.AmountMinor,
                BankReference: request.ReturnReference,
                EvidenceDocumentId: request.RemittanceDocId ?? string.Empty,
                Notes: null);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-007: POST /api/v1/finance/cash-advances/{advance_id}/close
        group.MapPost("/{advance_id}/close", async (
            string advance_id,
            CloseCashAdvanceApiRequest? request,
            HttpContext httpContext,
            ICurrentActor actor,
            CloseHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new CloseCashAdvanceCommand(
                AdvanceId: advance_id,
                ActorId: actor.ActorId,
                ClosureReason: request?.Remarks ?? "Authorized closure by controller",
                AuthorizedExceptionId: null);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-008: POST /api/v1/finance/cash-advances/{advance_id}/decide
        group.MapPost("/{advance_id}/decide", async (
            string advance_id,
            DecideCashAdvanceApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            DecideHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new DecideCashAdvanceCommand(
                AdvanceId: advance_id,
                ActorId: actor.ActorId,
                ActorRoles: actor.Roles.ToList(),
                Decision: request.Decision,
                ApprovedAmountMinor: request.ApprovedAmountMinor,
                Reason: request.RejectionReason ?? request.CorrectionNotes);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-009: POST /api/v1/finance/cash-advances/{advance_id}/disbursements
        group.MapPost("/{advance_id}/disbursements", async (
            string advance_id,
            PrepareDisbursementApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            PrepareHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new PrepareDisbursementCommand(
                AdvanceId: advance_id,
                MakerId: actor.ActorId,
                PaymentAmountMinor: request.PaymentAmountMinor ?? 0,
                PaymentMethod: request.TargetPaymentMethod,
                RecipientAccountRef: request.BankAccountNumber ?? request.BankAccountName,
                IdempotencyKey: request.IdempotencyKey);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-010: POST /api/v1/finance/cash-advances/{advance_id}/reimbursement-settle
        group.MapPost("/{advance_id}/reimbursement-settle", async (
            string advance_id,
            ReimbursementSettleApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            ReimbursementSettleHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new RecordReimbursementSettlementCommand(
                AdvanceId: advance_id,
                ActorId: actor.ActorId,
                SettledAmountMinor: request.AmountMinor,
                BankReference: request.BankReference,
                EvidenceDocumentId: request.RemittanceDocId ?? string.Empty,
                Notes: null);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-011: POST /api/v1/finance/cash-advances/{advance_id}/submit
        group.MapPost("/{advance_id}/submit", async (
            string advance_id,
            HttpContext httpContext,
            ICurrentActor actor,
            SubmitHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SubmitCashAdvanceCommand(
                AdvanceId: advance_id,
                ActorId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
