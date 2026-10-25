using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.ExpireAttempt;

public sealed class ExpireAttemptHandler : ICommandHandler<ExpireAttemptCommand, ExpireAttemptResponse>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IClock _clock;

    public ExpireAttemptHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(clock);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _clock = clock;
    }

    public async Task<Result<ExpireAttemptResponse>> HandleAsync(
        ExpireAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentId))
        {
            return Result.Failure<ExpireAttemptResponse>(
                Error.Validation("ExpireAttempt.IdRequired", "Payment ID is required."));
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
            return Result.Failure<ExpireAttemptResponse>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{command.PaymentId}' was not found."));
        }

        if (order is not null && order.Status == OrderStatus.Paid)
        {
            return Result.Failure<ExpireAttemptResponse>(
                Error.Validation("Payments.CannotExpirePaidOrder", $"Cannot expire payment attempt: Order '{order.OrderNumber}' is already settled and paid."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
        {
            await _paymentGateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
        }

        Result markResult = attempt.MarkExpired(nowUtc);
        if (markResult.IsFailure)
        {
            return Result.Failure<ExpireAttemptResponse>(markResult.Error);
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        return Result.Success(new ExpireAttemptResponse(
            Success: true,
            Status: "expired",
            Message: $"Payment attempt '{attempt.Id}' authoritatively expired.",
            Attempt: attempt.ToDto()));
    }
}
