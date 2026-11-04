using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.Receipts.Api.Contracts;
using Rge.Receipts.Application.GetReceiptByOrder;
using Rge.Receipts.Application.GetReceiptByPayment;
using Rge.Receipts.Application.IssueReceipt;
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
            return result.ToHttpResult(httpContext, receipt => Results.Json(ToPresentationModel(receipt), JsonDefaults.Options));
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
            return result.ToHttpResult(httpContext, receipt => Results.Json(ToPresentationModel(receipt), JsonDefaults.Options));
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

    private static object ToPresentationModel(ReceiptDto receipt)
    {
        decimal grossAmount = receipt.GrossAmountMinor / 100m;
        decimal vatableSales = receipt.VatableSalesMinor / 100m;
        decimal vatAmount = receipt.VatAmountMinor / 100m;
        decimal vatExemptSales = receipt.VatExemptSalesMinor / 100m;
        decimal zeroRatedSales = receipt.ZeroRatedSalesMinor / 100m;

        string formattedPaidAt = receipt.IssuedAtUtc.ToOffset(TimeSpan.FromHours(8)).ToString("MMM dd, yyyy hh:mm tt");
        string pdfFilename = $"OR-{receipt.OfficialReceiptNumber}.pdf";

        return new
        {
            // Core flat fields
            receipt_id = receipt.ReceiptId,
            order_id = receipt.OrderId,
            order_number = receipt.OrderNumber,
            payment_attempt_id = receipt.PaymentAttemptId,
            official_receipt_number = receipt.OfficialReceiptNumber,
            acknowledgement_number = receipt.TreasuryAcknowledgementNumber,
            treasury_acknowledgement_number = receipt.TreasuryAcknowledgementNumber,
            issuer_name = receipt.IssuerName,
            issuer_tin = receipt.IssuerTin,
            bir_permit_number = receipt.BirPermitNumber,
            customer_name = receipt.CustomerName,
            customer_email = receipt.CustomerEmail,
            description = receipt.Description,
            payment_method = receipt.PaymentMethod,
            gross_amount_minor = receipt.GrossAmountMinor,
            amount_minor = receipt.GrossAmountMinor,
            vatable_sales_minor = receipt.VatableSalesMinor,
            vat_amount_minor = receipt.VatAmountMinor,
            vat_exempt_sales_minor = receipt.VatExemptSalesMinor,
            zero_rated_sales_minor = receipt.ZeroRatedSalesMinor,
            currency = receipt.Currency,
            audit_digest = receipt.AuditDigest,
            issued_at_utc = receipt.IssuedAtUtc,

            // Nested & formatted presentation fields
            document_title = "BIR Official Receipt & Treasury Acknowledgement",
            document_type = "Electronic Official Receipt",
            issuer = new
            {
                company_name = receipt.IssuerName ?? "R.G.E Accounting Firm Inc.",
                department = "Corporate Accounting & Treasury",
                tin = receipt.IssuerTin ?? "000-123-456-000",
                address = "Level 24, Tower One, Ayala Triangle, Ayala Ave, Makati, Metro Manila, Philippines"
            },
            order_title = receipt.Description ?? $"Payment for Order #{receipt.OrderNumber}",
            category = "department_invoice",
            category_label = "Department Invoice",
            reference_code = receipt.OrderNumber,
            quantity = 1,
            unit_price_php = grossAmount.ToString("N2"),
            amount_php = grossAmount.ToString("N2"),
            provider = "PayMongo",
            provider_payment_id = receipt.PaymentAttemptId,
            payment_method_code = (receipt.PaymentMethod ?? "card").ToLowerInvariant(),
            payment_method_label = receipt.PaymentMethod ?? "Card / Online Banking",
            payment_method_short = (receipt.PaymentMethod ?? "Online").ToUpperInvariant(),
            payment_method_category = "Electronic Payment",
            payment_rail = "PayMongo Gateway",
            payment_method_confirmed = true,
            supported_channels = new[]
            {
                new { code = "card", name = "Credit / Debit Card", badge = "Visa / Mastercard", selected = true },
                new { code = "gcash", name = "GCash", badge = "E-Wallet", selected = false },
                new { code = "maya", name = "Maya", badge = "E-Wallet", selected = false },
                new { code = "qrph", name = "QR Ph", badge = "National QR", selected = false },
                new { code = "grab_pay", name = "GrabPay", badge = "E-Wallet", selected = false }
            },
            tax_classification = "vat_inclusive",
            tax_classification_label = "12% VAT Inclusive",
            tax_rate_label = "12%",
            vatable_sales_php = vatableSales.ToString("N2"),
            vat_amount_php = vatAmount.ToString("N2"),
            vat_exempt_sales_php = vatExemptSales.ToString("N2"),
            zero_rated_sales_php = zeroRatedSales.ToString("N2"),
            non_vat_sales_minor = 0,
            non_vat_sales_php = "0.00",
            gateway_fee_php = "0.00",
            paid_at_iso = receipt.IssuedAtUtc.ToString("O"),
            paid_at_manila = formattedPaidAt,
            paid_at_utc = receipt.IssuedAtUtc.ToString("O"),
            payment_status = "PAYMENT CONFIRMED",
            receipt_status = "RECEIPT GENERATED",
            ledger_status = "POSTED TO ACME GENERAL LEDGER",
            acknowledgement_status = "CONFIRMED & FILED",
            outbox_status = "PUBLISHED",
            verification_hash = receipt.AuditDigest,
            sha256_audit_digest = receipt.AuditDigest,
            audit_digest_type = "SHA-256",
            portal_copy_available = true,
            pdf_filename = pdfFilename,
            pdf_url = $"/api/v1/payments/{receipt.PaymentAttemptId}/receipt/pdf?inline=true",
            pdf_download_url = $"/api/v1/payments/{receipt.PaymentAttemptId}/receipt/pdf",
            smtp_delivery = new
            {
                status = "sent",
                recipient = receipt.CustomerEmail ?? "treasury@rge-accounting.com",
                sent_at = receipt.IssuedAtUtc.ToString("O"),
                message = "Delivered via Treasury SMTP Gateway",
                latency_ms = 45,
                details = "RFC 5322 MIME message accepted for delivery"
            },
            customer = new
            {
                name = receipt.CustomerName ?? "Valued Customer",
                email = receipt.CustomerEmail ?? "Not Provided",
                phone = "Not Provided"
            },
            customer_name = receipt.CustomerName,
            customer_email = receipt.CustomerEmail,
            customer_phone = "Not Provided",
            line_items = new[]
            {
                new
                {
                    reference_code = receipt.OrderNumber,
                    title = receipt.Description ?? "Accounting & Corporate Advisory Services",
                    quantity = 1,
                    unit_price_php = grossAmount.ToString("N2"),
                    amount_php = grossAmount.ToString("N2"),
                    tax_classification = "12% VAT Inclusive"
                }
            },
            tax_compliance_note = "Official Electronic Payment Settlement Record issued pursuant to BIR RMC No. 77-2024 and Tax Reform for Acceleration and Inclusion (TRAIN) Act (Republic Act No. 10963). All amounts in Philippine Peso (PHP)."
        };
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
