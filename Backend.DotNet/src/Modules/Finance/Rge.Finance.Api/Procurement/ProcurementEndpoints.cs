using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.PurchaseRequests.Create;
using Rge.Finance.Application.PurchaseRequests.Decide;
using Rge.Finance.Application.PurchaseRequests.GetById;
using Rge.Finance.Application.PurchaseRequests.List;
using Rge.Finance.Application.PurchaseRequests.Submit;

namespace Rge.Finance.Api.Procurement;

/// <summary>
/// Minimal API endpoints for Purchase Requests / Procurement (FIN-045 through FIN-049).
/// </summary>
public sealed class ProcurementEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapVersionedApiGroup("/api/v1/finance/purchase-requests", "Procurement");

        // FIN-045: GET /api/v1/finance/purchase-requests
        group.MapGet("/", async (
            string? department_id,
            string? status,
            int? limit,
            HttpContext httpContext,
            ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListPurchaseRequestsQuery(department_id, status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-046: POST /api/v1/finance/purchase-requests
        group.MapPost("/", async (
            CreatePurchaseRequestApiRequest request,
            HttpContext httpContext,
            CreateHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new CreatePurchaseRequestCommand(
                RequestorId: request.RequestorId,
                VendorId: request.VendorId,
                PaymentCategory: request.PaymentCategory,
                IsRestrictedCategory: request.IsRestrictedCategory,
                BusinessPurpose: request.BusinessPurpose,
                DepartmentId: request.DepartmentId,
                CostCenterId: request.CostCenterId,
                RequestedAmountMinor: request.RequestedAmountMinor,
                RequestedPaymentDate: request.RequestedPaymentDate,
                Currency: request.Currency ?? "PHP",
                BudgetReference: request.BudgetReference,
                DueDate: request.DueDate,
                SupportingDocIds: request.SupportingDocIds,
                InvoiceDocId: request.InvoiceDocId,
                Remarks: request.Remarks,
                IdempotencyKey: request.IdempotencyKey);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-047: GET /api/v1/finance/purchase-requests/{pr_id}
        group.MapGet("/{pr_id}", async (
            string pr_id,
            HttpContext httpContext,
            GetByIdHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetPurchaseRequestByIdQuery(pr_id);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-048: POST /api/v1/finance/purchase-requests/{pr_id}/decide
        group.MapPost("/{pr_id}/decide", async (
            string pr_id,
            DecidePurchaseRequestApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            DecideHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new DecidePurchaseRequestCommand(
                RequestId: pr_id,
                Decision: request.Decision,
                ActorId: actor.ActorId,
                ApprovedAmountMinor: request.ApprovedAmountMinor,
                Reason: request.Reason);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-049: POST /api/v1/finance/purchase-requests/{pr_id}/submit
        group.MapPost("/{pr_id}/submit", async (
            string pr_id,
            HttpContext httpContext,
            ICurrentActor actor,
            SubmitHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SubmitPurchaseRequestCommand(
                RequestId: pr_id,
                ActorId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
