using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.GetReceiptByOrder;

public sealed class GetReceiptByOrderHandler : IQueryHandler<GetReceiptByOrderQuery, ReceiptDto>
{
    private readonly IReceiptRepository _repository;

    public GetReceiptByOrderHandler(IReceiptRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<ReceiptDto>> HandleAsync(
        GetReceiptByOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.OrderId))
        {
            return Result.Failure<ReceiptDto>(
                Error.Validation("Receipts.InvalidOrderId", "Order ID is required."));
        }

        OfficialReceipt? receipt = await _repository
            .GetByOrderIdAsync(query.OrderId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return Result.Failure<ReceiptDto>(
                Error.NotFound("Receipts.NotFoundForOrder", $"No Official Receipt found for order '{query.OrderId}'."));
        }

        return Result.Success(IssueReceiptHandler.MapToDto(receipt));
    }
}

