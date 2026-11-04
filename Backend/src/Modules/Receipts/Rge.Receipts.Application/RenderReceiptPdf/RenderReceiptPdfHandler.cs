using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.RenderReceiptPdf;

public sealed class RenderReceiptPdfHandler : IQueryHandler<RenderReceiptPdfQuery, RenderedReceiptPdfDto>
{
    private readonly IReceiptRepository _repository;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IReceiptSourceResolver? _sourceResolver;
    private readonly IssueReceiptHandler? _issueReceiptHandler;

    public RenderReceiptPdfHandler(IReceiptRepository repository, IPdfRenderer pdfRenderer)
        : this(repository, pdfRenderer, sourceResolver: null, issueReceiptHandler: null)
    {
    }

    public RenderReceiptPdfHandler(
        IReceiptRepository repository,
        IPdfRenderer pdfRenderer,
        IReceiptSourceResolver? sourceResolver,
        IssueReceiptHandler? issueReceiptHandler)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(pdfRenderer);

        _repository = repository;
        _pdfRenderer = pdfRenderer;
        _sourceResolver = sourceResolver;
        _issueReceiptHandler = issueReceiptHandler;
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

        // Just-In-Time (JIT) Receipt Synthesis for PDF rendering
        if (receipt is null && _sourceResolver is not null && _issueReceiptHandler is not null)
        {
            ReceiptSourceInfo? source = null;
            if (!string.IsNullOrWhiteSpace(query.PaymentId))
            {
                source = await _sourceResolver.ResolveByPaymentAttemptIdAsync(query.PaymentId.Trim(), cancellationToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(query.OrderId))
            {
                source = await _sourceResolver.ResolveByOrderIdAsync(query.OrderId.Trim(), cancellationToken).ConfigureAwait(false);
            }

            if (source is not null && source.IsPaid)
            {
                var issueCommand = new IssueReceiptCommand(
                    OrderId: source.OrderId,
                    OrderNumber: source.OrderNumber,
                    PaymentAttemptId: source.PaymentAttemptId,
                    AmountMinor: source.AmountMinor,
                    Currency: source.Currency,
                    CustomerName: source.CustomerName,
                    CustomerEmail: source.CustomerEmail,
                    Description: source.Description,
                    PaymentMethod: source.PaymentMethod);

                var issueResult = await _issueReceiptHandler.HandleAsync(issueCommand, cancellationToken).ConfigureAwait(false);
                if (issueResult.IsSuccess)
                {
                    receipt = await _repository.GetByPaymentAttemptIdAsync(source.PaymentAttemptId, cancellationToken).ConfigureAwait(false)
                        ?? await _repository.GetByOrderIdAsync(source.OrderId, cancellationToken).ConfigureAwait(false);
                }
            }
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
