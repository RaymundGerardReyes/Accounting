using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.Disbursements.BankApprove;
using Rge.Finance.Application.Disbursements.Execute;
using Rge.Finance.Application.Disbursements.SetExceptionState;
using Rge.Finance.Application.Disbursements.Verify;
using Rge.Finance.Application.Disbursements.VerifyEvidence;

namespace Rge.Finance.Api.Disbursements;

/// <summary>
/// Minimal API endpoints for Bank Disbursement lifecycle (FIN-013 through FIN-017).
/// </summary>
public sealed class DisbursementEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapVersionedApiGroup("/api/v1/finance/disbursements", "Disbursements");

        // FIN-013: POST /api/v1/finance/disbursements/{disbursement_id}/bank-approve
        group.MapPost("/{disbursement_id}/bank-approve", async (
            string disbursement_id,
            HttpContext httpContext,
            ICurrentActor actor,
            BankApproveHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new BankApproveDisbursementCommand(
                DisbursementId: disbursement_id,
                BankApproverId: actor.ActorId,
                Roles: actor.Roles.ToList());

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-014: POST /api/v1/finance/disbursements/{disbursement_id}/exception-state
        group.MapPost("/{disbursement_id}/exception-state", async (
            string disbursement_id,
            SetDisbursementExceptionStateApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            SetExceptionStateHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SetDisbursementExceptionStateCommand(
                DisbursementId: disbursement_id,
                ActorId: actor.ActorId,
                TargetState: request.TargetState,
                Reason: request.Reason);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-015: POST /api/v1/finance/disbursements/{disbursement_id}/execute
        group.MapPost("/{disbursement_id}/execute", async (
            string disbursement_id,
            ExecuteDisbursementApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            ExecuteHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ExecuteDisbursementCommand(
                DisbursementId: disbursement_id,
                ExecutorId: actor.ActorId,
                BankReference: request.BankReference,
                RemittanceDocumentId: request.RemittanceDocId,
                ExecutedAmountMinor: request.ExecutedAmountMinor);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-016: POST /api/v1/finance/disbursements/{disbursement_id}/verify
        group.MapPost("/{disbursement_id}/verify", async (
            string disbursement_id,
            HttpContext httpContext,
            ICurrentActor actor,
            VerifyHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new VerifyDisbursementCommand(
                DisbursementId: disbursement_id,
                VerifierId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-017: POST /api/v1/finance/disbursements/{disbursement_id}/verify-evidence
        group.MapPost("/{disbursement_id}/verify-evidence", async (
            string disbursement_id,
            VerifyEvidenceDisbursementApiRequest? request,
            HttpContext httpContext,
            ICurrentActor actor,
            VerifyEvidenceHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new VerifyEvidenceCommand(
                DisbursementId: disbursement_id,
                EvidenceVerifierId: actor.ActorId,
                EvidenceDocId: request?.EvidenceDocumentId ?? string.Empty,
                Notes: request?.Notes);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
