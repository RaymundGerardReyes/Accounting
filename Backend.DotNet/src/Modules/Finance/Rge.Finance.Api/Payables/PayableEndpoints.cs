using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.Payables.BankApprovePayment;
using Rge.Finance.Application.Payables.ExecutePayment;
using Rge.Finance.Application.Payables.PreparePayment;
using Rge.Finance.Application.Payables.ReconcilePayment;
using Rge.Finance.Application.Payables.Register;
using Rge.Finance.Application.Payables.SetPaymentExceptionState;
using Rge.Finance.Application.Payables.VerifyPayment;

namespace Rge.Finance.Api.Payables;

/// <summary>
/// Minimal API endpoints for Accounts Payable & Payment Records (FIN-032 through FIN-039).
/// </summary>
public sealed class PayableEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder payablesGroup = app.MapVersionedApiGroup("/api/v1/finance/payables", "Payables");
        RouteGroupBuilder paymentsGroup = app.MapVersionedApiGroup("/api/v1/finance/payment-records", "PaymentRecords");

        // FIN-032: GET /api/v1/finance/payables
        payablesGroup.MapGet("/", async (
            string? vendor_id,
            string? status,
            int? limit,
            HttpContext httpContext,
            RegisterHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListPayablesQuery(vendor_id, status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-033: POST /api/v1/finance/payables
        payablesGroup.MapPost("/", async (
            RegisterPayableApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            RegisterHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new RegisterPayableCommand(
                PurchaseRequestId: request.PurchaseRequestId,
                VendorId: request.VendorId,
                InvoiceNumber: request.InvoiceNumber,
                InvoiceDate: request.InvoiceDate,
                DueDate: request.DueDate,
                OriginalPayableMinor: request.OriginalPayableMinor,
                Currency: request.Currency ?? "PHP",
                CreditAdjustmentMinor: request.CreditAdjustmentMinor,
                VatCode: request.VatCode,
                VatAmountMinor: request.VatAmountMinor,
                EwtCode: request.EwtCode,
                EwtAmountMinor: request.EwtAmountMinor,
                PaymentTerms: request.PaymentTerms,
                AccountingReference: request.AccountingReference,
                ValidatedById: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-034: POST /api/v1/finance/payables/{payable_id}/payments
        payablesGroup.MapPost("/{payable_id}/payments", async (
            string payable_id,
            PreparePayablePaymentApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            PreparePaymentHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new PreparePaymentCommand(
                PayableId: payable_id,
                PaymentAmountMinor: request.PaymentAmountMinor,
                PaymentMethod: request.PaymentMethod,
                MakerId: actor.ActorId,
                Currency: request.Currency ?? "PHP",
                IdempotencyKey: request.IdempotencyKey);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-035: POST /api/v1/finance/payment-records/{payment_id}/bank-approve
        paymentsGroup.MapPost("/{payment_id}/bank-approve", async (
            string payment_id,
            HttpContext httpContext,
            ICurrentActor actor,
            BankApprovePaymentHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new BankApprovePaymentCommand(
                PaymentId: payment_id,
                BankApproverId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-036: POST /api/v1/finance/payment-records/{payment_id}/exception-state
        paymentsGroup.MapPost("/{payment_id}/exception-state", async (
            string payment_id,
            SetPaymentRecordExceptionStateApiRequest request,
            HttpContext httpContext,
            SetPaymentExceptionStateHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SetPaymentExceptionStateCommand(
                PaymentId: payment_id,
                TargetState: request.TargetState,
                Reason: request.Reason);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-037: POST /api/v1/finance/payment-records/{payment_id}/execute
        paymentsGroup.MapPost("/{payment_id}/execute", async (
            string payment_id,
            ExecutePaymentRecordApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            ExecutePaymentHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ExecutePaymentCommand(
                PaymentId: payment_id,
                ExecutedById: actor.ActorId,
                ExecutionDate: request.ExecutionDate,
                BankReference: request.BankReference,
                RemittanceDocId: request.RemittanceDocId,
                ExecutedAmountMinor: request.ExecutedAmountMinor);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-038: POST /api/v1/finance/payment-records/{payment_id}/reconcile
        paymentsGroup.MapPost("/{payment_id}/reconcile", async (
            string payment_id,
            ReconcilePaymentRecordApiRequest? request,
            HttpContext httpContext,
            ICurrentActor actor,
            ReconcilePaymentHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ReconcilePaymentCommand(
                PaymentId: payment_id,
                EvidenceVerifierId: actor.ActorId,
                Notes: request?.Notes);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-039: POST /api/v1/finance/payment-records/{payment_id}/verify
        paymentsGroup.MapPost("/{payment_id}/verify", async (
            string payment_id,
            HttpContext httpContext,
            ICurrentActor actor,
            VerifyPaymentHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new VerifyPaymentCommand(
                PaymentId: payment_id,
                VerifierId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
