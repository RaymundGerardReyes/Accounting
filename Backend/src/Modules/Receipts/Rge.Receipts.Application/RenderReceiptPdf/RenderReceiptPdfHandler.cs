using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.RenderReceiptPdf;

public sealed class RenderReceiptPdfHandler : IQueryHandler<RenderReceiptPdfQuery, RenderedReceiptPdfDto>
{
    private readonly IReceiptRepository _repository;
    private readonly IPdfRenderer _pdfRenderer;

    public RenderReceiptPdfHandler(IReceiptRepository repository, IPdfRenderer pdfRenderer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(pdfRenderer);

        _repository = repository;
        _pdfRenderer = pdfRenderer;
    }

    public async Task<Result<RenderedReceiptPdfDto>> HandleAsync(
        RenderReceiptPdfQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        OfficialReceipt? receipt = null;
        if (!string.IsNullOrWhiteSpace(query.PaymentId))
        {
            receipt = await _repository.GetByPaymentAttemptIdAsync(query.PaymentId.Trim(), cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(query.OrderId))
        {
            receipt = await _repository.GetByOrderIdAsync(query.OrderId.Trim(), cancellationToken).ConfigureAwait(false);
        }

        if (receipt is null)
        {
            return Result.Failure<RenderedReceiptPdfDto>(
                Error.NotFound("Receipts.PdfSourceNotFound", "Cannot render PDF: Official Receipt was not found."));
        }

        byte[] pdfBytes = _pdfRenderer.RenderOfficialReceiptPdf(receipt);
        string fileName = $"{receipt.ReceiptNumber.Value}.pdf";

        return Result.Success(new RenderedReceiptPdfDto(fileName, "application/pdf", pdfBytes));
    }
}

