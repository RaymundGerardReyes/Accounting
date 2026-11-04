using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.GetReceiptByOrder;

public sealed class GetReceiptByOrderHandler : IQueryHandler<GetReceiptByOrderQuery, ReceiptDto>
{
    private readonly IReceiptRepository _repository;
    private readonly IReceiptSourceResolver? _sourceResolver;
    private readonly IssueReceiptHandler? _issueReceiptHandler;

    public GetReceiptByOrderHandler(IReceiptRepository repository)
        : this(repository, sourceResolver: null, issueReceiptHandler: null)
    {
    }

    public GetReceiptByOrderHandler(
        IReceiptRepository repository,
        IReceiptSourceResolver? sourceResolver,
        IssueReceiptHandler? issueReceiptHandler)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _sourceResolver = sourceResolver;
        _issueReceiptHandler = issueReceiptHandler;
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

        string orderId = query.OrderId.Trim();

        OfficialReceipt? receipt = await _repository
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (receipt is not null)
        {
            return Result.Success(IssueReceiptHandler.MapToDto(receipt));
        }

        // Just-In-Time (JIT) Receipt Synthesis: if order is confirmed paid, issue receipt on demand
        if (_sourceResolver is not null && _issueReceiptHandler is not null)
        {
            ReceiptSourceInfo? source = await _sourceResolver
                .ResolveByOrderIdAsync(orderId, cancellationToken)
                .ConfigureAwait(false);

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

                Result<ReceiptDto> issueResult = await _issueReceiptHandler
                    .HandleAsync(issueCommand, cancellationToken)
                    .ConfigureAwait(false);

                if (issueResult.IsSuccess)
                {
                    return issueResult;
                }
            }
        }

        return Result.Failure<ReceiptDto>(
            Error.NotFound("Receipts.NotFoundForOrder", $"No Official Receipt found for order '{query.OrderId}'."));
    }
}
