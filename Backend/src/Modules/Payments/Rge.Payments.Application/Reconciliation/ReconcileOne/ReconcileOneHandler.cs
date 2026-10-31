using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.Reconciliation.ReconcileOne;

public sealed class ReconcileOneHandler : ICommandHandler<ReconcileOneCommand, ReconcileOneResponse>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IClock _clock;

    public ReconcileOneHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IOutboxWriter outboxWriter,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(clock);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _outboxWriter = outboxWriter;
        _clock = clock;
    }

    public async Task<Result<ReconcileOneResponse>> HandleAsync(
        ReconcileOneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentId))
        {
            return Result.Failure<ReconcileOneResponse>(
                Error.Validation("ReconcileOne.IdRequired", "Payment ID is required."));
        }

        string rawId = command.PaymentId.Trim();
        PaymentAttempt? attempt = await _attemptRepository.GetByIdAsync(rawId, cancellationToken).ConfigureAwait(false)
            ?? await _attemptRepository.GetByProviderPaymentIdAsync(rawId, cancellationToken).ConfigureAwait(false);

        if (attempt is null)
        {
            return Result.Failure<ReconcileOneResponse>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{command.PaymentId}' was not found."));
        }

        if (PaymentAttemptStateMachine.IsTerminal(attempt.Status))
        {
            return Result.Success(new ReconcileOneResponse(
                Status: $"already_{attempt.Status.ToString().ToLowerInvariant()}",
                PaymentId: attempt.Id,
                OrderId: attempt.OrderId,
                Attempt: attempt.ToDto(),
                Message: $"Payment attempt '{attempt.Id}' is already in terminal state '{attempt.Status}'."));
        }

        if (string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
        {
            return Result.Success(new ReconcileOneResponse(
                Status: "skipped_no_provider_id",
                PaymentId: attempt.Id,
                OrderId: attempt.OrderId,
                Attempt: attempt.ToDto(),
                Message: $"Payment attempt '{attempt.Id}' has no remote provider payment ID."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result<GatewayPaymentStatusResult> gwResult = await _paymentGateway
            .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (gwResult.IsFailure)
        {
            return Result.Failure<ReconcileOneResponse>(gwResult.Error);
        }

        var gw = gwResult.Value;
        string gwStatus = gw.Status.ToLowerInvariant();

        Order? order = await _orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);

        if (gwStatus is "paid" or "succeeded")
        {
            DateTimeOffset paidAt = gw.PaidAtUtc ?? nowUtc;
            attempt.MarkSucceeded(paidAt, gw.PaymentMethodUsed);

            if (order is not null && order.Status != OrderStatus.Paid)
            {
                order.MarkPaid(paidAt);
                await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                var paidEvent = OrderPaidIntegrationEvent.Create(
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    paymentAttemptId: attempt.Id,
                    amountMinor: order.Amount.AmountMinor,
                    currency: order.Amount.CurrencyCode,
                    paymentMethod: attempt.PaymentMethod,
                    customerName: "Valued Customer",
                    customerEmail: string.Empty,
                    paidAtUtc: paidAt);

                await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (gwStatus is "expired")
        {
            attempt.MarkExpired(nowUtc);
        }
        else if (gwStatus is "failed")
        {
            attempt.MarkFailed(gw.FailureCode ?? "FAILED", gw.FailureMessage ?? "Payment failed", nowUtc);
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        return Result.Success(new ReconcileOneResponse(
            Status: "reconciled",
            PaymentId: attempt.Id,
            OrderId: attempt.OrderId,
            Attempt: attempt.ToDto(),
            Message: $"Payment attempt '{attempt.Id}' reconciled with status '{attempt.Status}'."));
    }
}
