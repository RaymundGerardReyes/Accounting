using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Receipts.Application.RenderReceiptPdf;

public sealed record RenderReceiptPdfQuery(
    string? PaymentId = null,
    string? OrderId = null) : IQuery<RenderedReceiptPdfDto>;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary PDF document payload")]
public sealed record RenderedReceiptPdfDto(
    string FileName,
    string ContentType,
    byte[] PdfBytes);
