using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.ExceptionRequests.Decide;
using Rge.Finance.Application.ExceptionRequests.List;
using Rge.Finance.Application.ExceptionRequests.Submit;
using Rge.Finance.Application.Reporting.ExceptionRegister;

namespace Rge.Finance.Api.Exceptions;

/// <summary>
/// Minimal API endpoints for Operational Exception Register & Policy Exception Requests (FIN-018 through FIN-022).
/// </summary>
public sealed class ExceptionEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder registerGroup = app.MapVersionedApiGroup("/api/v1/finance/exception-register", "ExceptionRegister");
        RouteGroupBuilder requestsGroup = app.MapVersionedApiGroup("/api/v1/finance/exception-requests", "ExceptionRequests");

        // FIN-018: GET /api/v1/finance/exception-register
        registerGroup.MapGet("/", async (
            string? status,
            int? limit,
            HttpContext httpContext,
            ExceptionRegisterHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListExceptionRegisterQuery(status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-019: POST /api/v1/finance/exception-register/{register_id}/resolve
        registerGroup.MapPost("/{register_id}/resolve", async (
            string register_id,
            ResolveExceptionRegisterItemApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            ExceptionRegisterHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ResolveExceptionRegisterItemCommand(
                RegisterId: register_id,
                ResolverId: actor.ActorId,
                ResolutionNotes: request.Notes ?? request.ResolutionStatus);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-020: GET /api/v1/finance/exception-requests
        requestsGroup.MapGet("/", async (
            string? employee_id,
            string? status,
            int? limit,
            HttpContext httpContext,
            ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListExceptionRequestsQuery(employee_id, status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-021: POST /api/v1/finance/exception-requests
        requestsGroup.MapPost("/", async (
            SubmitExceptionRequestApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            SubmitHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SubmitExceptionRequestCommand(
                EmployeeId: request.EmployeeId,
                RequesterId: actor.ActorId,
                TargetEntityType: request.TargetEntityType,
                TargetEntityId: request.TargetEntityId,
                ReportingPeriod: request.ReportingPeriod,
                PolicyVersionId: request.PolicyVersionId,
                ViolatedRuleCode: request.ViolatedRuleCode,
                ExceptionType: request.ExceptionType,
                RequestedAmountMinor: request.RequestedAmountMinor,
                Currency: request.Currency ?? "PHP",
                BusinessJustification: request.BusinessJustification,
                SupportingDocumentIds: request.SupportingDocumentIds);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-022: POST /api/v1/finance/exception-requests/{exception_id}/decide
        requestsGroup.MapPost("/{exception_id}/decide", async (
            string exception_id,
            DecideExceptionRequestApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            DecideHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new DecideExceptionRequestCommand(
                ExceptionId: exception_id,
                ApproverId: actor.ActorId,
                Roles: actor.Roles.ToList(),
                Decision: request.Decision,
                ApprovedAmountMinor: null,
                Reason: request.Reason,
                ExpiresAtUtc: null);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
