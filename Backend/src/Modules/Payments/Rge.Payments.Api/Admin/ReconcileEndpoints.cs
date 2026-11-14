using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Reconciliation.ReconcileAllNonTerminal;
using Rge.Payments.Application.Reconciliation.ReconcileOne;

namespace Rge.Payments.Api.Admin;

public sealed class ReconcileEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/admin/payments/{payment_id}/reconcile", ReconcileOneAsync)
           .WithName("ReconcilePayment")
           .WithTags("Payments.Admin");

        app.MapPost("/api/v1/admin/payments/reconcile-all", ReconcileAllAsync)
           .WithName("ReconcileAllPayments")
           .WithTags("Payments.Admin");
    }

    private static async Task<IResult> ReconcileOneAsync(
        HttpContext httpContext,
        string payment_id,
        ReconcileOneHandler handler,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!actor.IsInRole("admin") && !actor.IsInRole("finance_admin"))
        {
            return Results.Forbid();
        }

        var command = new ReconcileOneCommand(payment_id);
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }

    private static async Task<IResult> ReconcileAllAsync(
        HttpContext httpContext,
        ReconcileAllNonTerminalHandler handler,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!actor.IsInRole("admin") && !actor.IsInRole("finance_admin"))
        {
            return Results.Forbid();
        }

        var command = new ReconcileAllNonTerminalCommand();
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }
}
