using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.ResendReceiptEmail;

public sealed class ResendReceiptEmailHandler : ICommandHandler<ResendReceiptEmailCommand, ResendReceiptEmailResponse>
{
    private readonly IReceiptRepository _repository;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IReceiptMailer _mailer;

    public ResendReceiptEmailHandler(
        IReceiptRepository repository,
        IPdfRenderer pdfRenderer,
        IReceiptMailer mailer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(pdfRenderer);
        ArgumentNullException.ThrowIfNull(mailer);

        _repository = repository;
        _pdfRenderer = pdfRenderer;
        _mailer = mailer;
    }

    public async Task<Result<ResendReceiptEmailResponse>> HandleAsync(
        ResendReceiptEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        OfficialReceipt? receipt = await _repository
            .GetByPaymentAttemptIdAsync(command.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return Result.Failure<ResendReceiptEmailResponse>(
                Error.NotFound("Receipts.NotFound", $"Official Receipt for payment '{command.PaymentId}' was not found."));
        }

        string recipient = !string.IsNullOrWhiteSpace(command.OverrideRecipientEmail)
            ? command.OverrideRecipientEmail.Trim()
            : receipt.CustomerEmail;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return Result.Failure<ResendReceiptEmailResponse>(
                Error.Validation("Receipts.MissingRecipientEmail", "A recipient email address is required."));
        }

        byte[] pdfBytes = _pdfRenderer.RenderOfficialReceiptPdf(receipt);
        Result mailResult = await _mailer
            .SendReceiptEmailAsync(receipt, recipient, pdfBytes, cancellationToken)
            .ConfigureAwait(false);

        if (mailResult.IsFailure)
        {
            return Result.Failure<ResendReceiptEmailResponse>(mailResult.Error);
        }

        return Result.Success(new ResendReceiptEmailResponse(
            PaymentId: command.PaymentId,
            OfficialReceiptNumber: receipt.ReceiptNumber.Value,
            RecipientEmail: recipient,
            Dispatched: true));
    }
}
