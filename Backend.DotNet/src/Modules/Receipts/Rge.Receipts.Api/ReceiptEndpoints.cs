using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Receipts.Api.Contracts;
using Rge.Receipts.Application.GetReceiptByOrder;
using Rge.Receipts.Application.GetReceiptByPayment;
using Rge.Receipts.Application.RenderReceiptPdf;
using Rge.Receipts.Application.ResendReceiptEmail;

namespace Rge.Receipts.Api;

/// <summary>
/// Mounts BIR Official Receipt and Treasury Acknowledgement endpoints (#15-#19):
/// - GET  /api/v1/payments/{payment_id}/receipt
/// - GET  /api/v1/payments/{payment_id}/receipt/pdf
/// - POST /api/v1/payments/{payment_id}/receipt/email
/// - GET  /api/v1/orders/{order_id}/receipt
/// - GET  /api/v1/orders/{order_id}/receipt/pdf
/// </summary>
public sealed class ReceiptEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // #15: GET /api/v1/payments/{payment_id}/receipt
        app.MapGet("/api/v1/payments/{payment_id}/receipt", async (
            string payment_id,
            HttpContext httpContext,
            GetReceiptByPaymentHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetReceiptByPaymentQuery(payment_id), ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Receipts");

        // #16: GET /api/v1/payments/{payment_id}/receipt/pdf
        app.MapGet("/api/v1/payments/{payment_id}/receipt/pdf", async (
            string payment_id,
            [FromQuery(Name = "inline")] bool? inline,
            HttpContext httpContext,
            RenderReceiptPdfHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RenderReceiptPdfQuery(PaymentId: payment_id), ct).ConfigureAwait(false);
            return result.ToHttpResult(
                httpContext,
                pdf => BuildPdfFileResult(pdf, inline == true, httpContext));
        }).WithTags("Receipts");

        // #17: POST /api/v1/payments/{payment_id}/receipt/email
        app.MapPost("/api/v1/payments/{payment_id}/receipt/email", async (
            string payment_id,
            ResendReceiptEmailRequest? request,
            HttpContext httpContext,
            ResendReceiptEmailHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new ResendReceiptEmailCommand(payment_id, request?.Email);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Receipts");

        // #18: GET /api/v1/orders/{order_id}/receipt
        app.MapGet("/api/v1/orders/{order_id}/receipt", async (
            string order_id,
            HttpContext httpContext,
            GetReceiptByOrderHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetReceiptByOrderQuery(order_id), ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Receipts");

        // #19: GET /api/v1/orders/{order_id}/receipt/pdf
        app.MapGet("/api/v1/orders/{order_id}/receipt/pdf", async (
            string order_id,
            [FromQuery(Name = "inline")] bool? inline,
            HttpContext httpContext,
            RenderReceiptPdfHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RenderReceiptPdfQuery(OrderId: order_id), ct).ConfigureAwait(false);
            return result.ToHttpResult(
                httpContext,
                pdf => BuildPdfFileResult(pdf, inline == true, httpContext));
        }).WithTags("Receipts");
    }

    private static IResult BuildPdfFileResult(
        RenderedReceiptPdfDto pdfResponse,
        bool renderInline,
        HttpContext httpContext)
    {
        if (renderInline)
        {
            httpContext.Response.Headers.ContentDisposition = $"inline; filename=\"{pdfResponse.FileName}\"";
            return Results.Bytes(pdfResponse.PdfBytes, contentType: pdfResponse.ContentType);
        }

        return Results.File(
            fileContents: pdfResponse.PdfBytes,
            contentType: pdfResponse.ContentType,
            fileDownloadName: pdfResponse.FileName);
    }
}
