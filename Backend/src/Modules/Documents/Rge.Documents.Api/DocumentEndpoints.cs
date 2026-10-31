using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Documents.Application.DownloadDocument;
using Rge.Documents.Application.ListDocuments;
using Rge.Documents.Application.UploadDocument;

namespace Rge.Documents.Api;

public sealed class DocumentEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Endpoint #52: POST /api/v1/finance/documents
        app.MapPost("/api/v1/finance/documents", async (
            HttpContext httpContext,
            UploadDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (!httpContext.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Multipart form-data is required." });
            }

            IFormCollection form = await httpContext.Request.ReadFormAsync(ct).ConfigureAwait(false);
            IFormFile? file = form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "File attachment is required." });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);

            var cmd = new UploadDocumentCommand(
                OriginalFilename: file.FileName,
                FileBytes: ms.ToArray(),
                UploadedByActorId: httpContext.User.Identity?.Name ?? "emp_field_01");

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, StatusCodes.Status201Created);
        }).WithTags("Documents");

        // Endpoint #53: GET /api/v1/finance/documents
        app.MapGet("/api/v1/finance/documents", async (
            HttpContext httpContext,
            ListDocumentsHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ListDocumentsQuery(), ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Documents");

        // Endpoint #54: GET /api/v1/finance/documents/{doc_id}/download
        app.MapGet("/api/v1/finance/documents/{doc_id}/download", async (
            string doc_id,
            HttpContext httpContext,
            DownloadDocumentHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DownloadDocumentQuery(doc_id), ct).ConfigureAwait(false);
            return result.ToHttpResult(
                httpContext,
                doc => Results.File(
                    doc.Content,
                    doc.Metadata.MimeType,
                    doc.Metadata.OriginalFilename));
        }).WithTags("Documents");
    }
}

