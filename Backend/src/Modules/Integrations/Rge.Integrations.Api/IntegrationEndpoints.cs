using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Integrations.Application.IntegrationStatusProbe;
using Rge.Integrations.Application.SaveCredentials;
using Rge.Integrations.Application.TestPayMongo;
using Rge.Integrations.Application.TestSmtp;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Api;

/// <summary>
/// Minimal API endpoints for platform integrations (INT-001, INT-002, INT-005, INT-006).
/// </summary>
public sealed class IntegrationEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // INT-001: POST /api/v1/integrations/configure
        app.MapPost("/api/v1/integrations/configure", async (
            SaveCredentialsRequest request,
            HttpContext httpContext,
            SaveCredentialsHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SaveCredentialsCommand(request);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");

        // INT-002: POST /api/v1/integrations/email/test
        app.MapPost("/api/v1/integrations/email/test", async (
            EmailTestRequest request,
            HttpContext httpContext,
            TestSmtpHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new TestSmtpCommand(request);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");

        // INT-005: POST /api/v1/integrations/paymongo/test
        app.MapPost("/api/v1/integrations/paymongo/test", async (
            PayMongoTestRequest request,
            HttpContext httpContext,
            TestPayMongoHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new TestPayMongoCommand(request);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");

        // INT-006: GET /api/v1/integrations/status
        app.MapGet("/api/v1/integrations/status", async (
            HttpContext httpContext,
            IntegrationStatusProbeHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetIntegrationStatusQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");
    }
}

