using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.GetReceiptByPayment;

public sealed class GetReceiptByPaymentHandler : IQueryHandler<GetReceiptByPaymentQuery, ReceiptDto>
{
    private readonly IReceiptRepository _repository;

    public GetReceiptByPaymentHandler(IReceiptRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<ReceiptDto>> HandleAsync(
        GetReceiptByPaymentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.PaymentId))
        {
            return Result.Failure<ReceiptDto>(
                Error.Validation("Receipts.InvalidPaymentId", "Payment ID is required."));
        }

        OfficialReceipt? receipt = await _repository
            .GetByPaymentAttemptIdAsync(query.PaymentId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return Result.Failure<ReceiptDto>(
                Error.NotFound("Receipts.NotFoundForPayment", $"No Official Receipt found for payment '{query.PaymentId}'."));
        }

        return Result.Success(IssueReceiptHandler.MapToDto(receipt));
    }
}

