using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.Policies.ApproveVersion;
using Rge.Finance.Application.Policies.AuthorVersion;
using Rge.Finance.Application.Policies.List;
using Rge.Finance.Application.Policies.RetireVersion;
using Rge.Finance.Application.Policies.SubmitVersion;

namespace Rge.Finance.Api.Policies;

/// <summary>
/// Minimal API endpoints for Financial Policies & Versioning (FIN-040 through FIN-044).
/// </summary>
public sealed class PolicyEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapVersionedApiGroup("/api/v1/finance/policies", "Policies");

        // FIN-040: GET /api/v1/finance/policies
        group.MapGet("/", async (
            HttpContext httpContext,
            ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListPoliciesQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-041: POST /api/v1/finance/policies/{policy_code}/versions
        group.MapPost("/{policy_code}/versions", async (
            string policy_code,
            AuthorPolicyVersionApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            AuthorVersionHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new AuthorPolicyVersionCommand(
                PolicyCode: policy_code,
                AuthorId: actor.ActorId,
                VersionNumber: request.VersionNumber,
                EffectiveFrom: request.EffectiveFrom,
                AggregateMonthlyLimitMinor: request.AggregateMonthlyLimitMinor,
                Currency: request.Currency ?? "PHP",
                MaxReportsPerPeriod: request.MaxReportsPerPeriod,
                ChangeJustification: request.ChangeJustification,
                EffectiveUntil: request.EffectiveUntil,
                PeriodType: request.PeriodType,
                SubmissionDeadlineDay: request.SubmissionDeadlineDay,
                DeadlineInterpretation: request.DeadlineInterpretation,
                FinanceSlaDays: request.FinanceSlaDays,
                SlaDayType: request.SlaDayType,
                RequireConsolidatedReceiptPdf: request.RequireConsolidatedReceiptPdf,
                RequireMfarForOutOfBase: request.RequireMfarForOutOfBase,
                AllowPartialLineApproval: request.AllowPartialLineApproval,
                BlockNewAdvanceWhenOverdue: request.BlockNewAdvanceWhenOverdue,
                MaxAdvanceAmountMinor: request.MaxAdvanceAmountMinor);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-042: POST /api/v1/finance/policies/versions/{version_id}/approve
        group.MapPost("/versions/{version_id}/approve", async (
            string version_id,
            ApprovePolicyVersionApiRequest? request,
            HttpContext httpContext,
            ICurrentActor actor,
            ApproveVersionHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ApprovePolicyVersionCommand(
                VersionId: version_id,
                ApproverId: actor.ActorId,
                Comments: request?.Comments);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-043: POST /api/v1/finance/policies/versions/{version_id}/retire
        group.MapPost("/versions/{version_id}/retire", async (
            string version_id,
            RetirePolicyVersionApiRequest? request,
            HttpContext httpContext,
            ICurrentActor actor,
            RetireVersionHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new RetirePolicyVersionCommand(
                VersionId: version_id,
                ActorId: actor.ActorId,
                Reason: request?.Reason);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-044: POST /api/v1/finance/policies/versions/{version_id}/submit
        group.MapPost("/versions/{version_id}/submit", async (
            string version_id,
            HttpContext httpContext,
            ICurrentActor actor,
            SubmitVersionHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SubmitPolicyVersionCommand(
                VersionId: version_id,
                ActorId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
