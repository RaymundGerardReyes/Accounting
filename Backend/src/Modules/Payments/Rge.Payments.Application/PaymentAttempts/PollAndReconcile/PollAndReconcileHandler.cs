using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.PollAndReconcile;

public sealed class PollAndReconcileHandler : IQueryHandler<PollAndReconcileQuery, PaymentAttemptDto>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IClock _clock;

    public PollAndReconcileHandler(
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

    public async Task<Result<PaymentAttemptDto>> HandleAsync(
        PollAndReconcileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.PaymentId))
        {
            return Result.Failure<PaymentAttemptDto>(
                Error.Validation("PollAndReconcile.IdRequired", "Payment ID is required."));
        }

        string rawId = query.PaymentId.Trim();

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
            return Result.Failure<PaymentAttemptDto>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{query.PaymentId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;

        if (query.AutoReconcile &&
            !string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
            !PaymentAttemptStateMachine.IsTerminal(attempt.Status))
        {
            Result<GatewayPaymentStatusResult> statusResult = await _paymentGateway
                .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsSuccess)
            {
                var gw = statusResult.Value;
                string gwStatus = gw.Status.ToLowerInvariant();

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
            }
        }

        if (order is not null && order.Status == OrderStatus.Paid && attempt.Status != PaymentAttemptStatus.Succeeded)
        {
            attempt.MarkSucceeded(order.UpdatedAt, attempt.PaymentMethod);
            await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(attempt.ToDto());
    }
}
