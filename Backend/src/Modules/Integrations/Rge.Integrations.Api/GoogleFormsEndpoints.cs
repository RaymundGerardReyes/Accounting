using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Integrations.Application.GetFormStatus;
using Rge.Integrations.Application.SubmitGoogleForm;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Api;

/// <summary>
/// Minimal API endpoints for Google Forms Integration (INT-003, INT-004).
/// </summary>
public sealed class GoogleFormsEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // INT-003: GET /api/v1/integrations/google-forms/status
        app.MapGet("/api/v1/integrations/google-forms/status", async (
            HttpContext httpContext,
            GetFormStatusHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetGoogleFormsStatusQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("GoogleForms");

        // INT-004: POST /api/v1/integrations/google-forms/submit
        app.MapPost("/api/v1/integrations/google-forms/submit", async (
            GoogleFormSubmissionRequest request,
            HttpContext httpContext,
            SubmitGoogleFormHandler handler,
            CancellationToken ct) =>
        {
            string? secret = httpContext.Request.Headers["X-Google-Forms-Secret"].FirstOrDefault()
                ?? httpContext.Request.Headers["x-google-forms-secret"].FirstOrDefault()
                ?? httpContext.Request.Query["secret"].FirstOrDefault();

            var cmd = new SubmitGoogleFormCommand(request, secret);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, StatusCodes.Status201Created);
        }).WithTags("GoogleForms");
    }
}

