using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.FailAttempt;

public sealed class FailAttemptHandler : ICommandHandler<FailAttemptCommand, FailAttemptResponse>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;

    public FailAttemptHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _clock = clock;
    }

    public async Task<Result<FailAttemptResponse>> HandleAsync(
        FailAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentId))
        {
            return Result.Failure<FailAttemptResponse>(
                Error.Validation("FailAttempt.IdRequired", "Payment ID is required."));
        }

        string rawId = command.PaymentId.Trim();

        PaymentAttempt? attempt = await _attemptRepository.GetByIdAsync(rawId, cancellationToken).ConfigureAwait(false)
            ?? await _attemptRepository.GetByProviderPaymentIdAsync(rawId, cancellationToken).ConfigureAwait(false);

        Order? order = null;
        if (attempt is not null)
        {
            order = await _orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            order = await _orderRepository.GetByIdAsync(rawId, cancellationToken).ConfigureAwait(false)
                ?? await _orderRepository.GetByOrderNumberAsync(rawId, cancellationToken).ConfigureAwait(false);

            if (order is not null)
            {
                attempt = await _attemptRepository.GetLatestByOrderIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        if (attempt is null)
        {
            return Result.Failure<FailAttemptResponse>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{command.PaymentId}' was not found."));
        }

        if (order is not null && order.Status == OrderStatus.Paid)
        {
            return Result.Failure<FailAttemptResponse>(
                Error.Validation("Payments.CannotFailPaidOrder", $"Cannot fail payment attempt: Order '{order.OrderNumber}' is already settled and paid."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string code = string.IsNullOrWhiteSpace(command.FailureCode) ? FailureCode.CardDeclined : command.FailureCode.Trim();
        string message = string.IsNullOrWhiteSpace(command.FailureMessage) ? "Payment declined by rail." : command.FailureMessage.Trim();

        Result markResult = attempt.MarkFailed(code, message, nowUtc);
        if (markResult.IsFailure)
        {
            return Result.Failure<FailAttemptResponse>(markResult.Error);
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        return Result.Success(new FailAttemptResponse(
            Success: true,
            Status: "failed",
            Message: $"Payment attempt '{attempt.Id}' marked failed.",
            Attempt: attempt.ToDto()));
    }
}
