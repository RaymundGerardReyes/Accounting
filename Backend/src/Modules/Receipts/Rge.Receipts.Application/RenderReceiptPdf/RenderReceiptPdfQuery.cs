using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Receipts.Application.RenderReceiptPdf;

public sealed record RenderReceiptPdfQuery(
    string? PaymentId = null,
    string? OrderId = null) : IQuery<RenderReceiptPdfResponse>, IQuery<RenderedReceiptPdfDto>;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary PDF document payload")]
public sealed record RenderReceiptPdfResponse(
    string ReceiptId,
    string ReceiptNumber,
    string FileName,
    string ContentType,
    byte[] PdfBytes,
    string AuditDigest);

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary PDF document payload")]
public sealed record RenderedReceiptPdfDto(
    string FileName,
    string ContentType,
    byte[] PdfBytes)
{
    public static RenderedReceiptPdfDto FromRenderReceiptPdfResponse(RenderReceiptPdfResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new(response.FileName, response.ContentType, response.PdfBytes);
    }

    public static implicit operator RenderedReceiptPdfDto(RenderReceiptPdfResponse response) =>
        FromRenderReceiptPdfResponse(response);
}
