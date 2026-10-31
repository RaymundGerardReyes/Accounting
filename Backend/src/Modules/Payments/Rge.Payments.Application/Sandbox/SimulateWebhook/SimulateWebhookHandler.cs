using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Application.Sandbox.SimulateWebhook;

public sealed class SimulateWebhookHandler : ICommandHandler<SimulateWebhookCommand, SimulateWebhookResponse>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IWebhookEventStore _webhookStore;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IWebhookSigningKeyProvider _secretProvider;
    private readonly IClock _clock;

    public SimulateWebhookHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IWebhookEventStore webhookStore,
        IOutboxWriter outboxWriter,
        IWebhookSigningKeyProvider secretProvider,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(webhookStore);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(clock);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _webhookStore = webhookStore;
        _outboxWriter = outboxWriter;
        _secretProvider = secretProvider;
        _clock = clock;
    }

    public async Task<Result<SimulateWebhookResponse>> HandleAsync(
        SimulateWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentAttemptId))
        {
            return Result.Failure<SimulateWebhookResponse>(
                Error.Validation("SimulateWebhook.IdRequired", "Payment attempt ID is required."));
        }

        string rawId = command.PaymentAttemptId.Trim();
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
            return Result.Failure<SimulateWebhookResponse>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{command.PaymentAttemptId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        long unixNow = nowUtc.ToUnixTimeSeconds();
        string simulatedEventId = $"evt_sim_{Guid.NewGuid():N}";
        string csId = attempt.ProviderPaymentId ?? $"cs_sim_{attempt.Id[..8]}";

        var payloadObj = new
        {
            data = new
            {
                id = simulatedEventId,
                type = "event",
                attributes = new
                {
                    type = command.EventType,
                    livemode = false,
                    created_at = unixNow,
                    data = new
                    {
                        id = csId,
                        type = "checkout_session",
                        attributes = new
                        {
                            status = command.EventType == "checkout_session.payment.paid" ? "paid" : "active",
                            payment_intent_id = $"pi_sim_{attempt.Id[..8]}",
                            payments = new[]
                            {
                                new
                                {
                                    id = $"pay_sim_{attempt.Id[..8]}",
                                    attributes = new
                                    {
                                        status = command.EventType == "checkout_session.payment.paid" ? "paid" : "failed",
                                        amount = attempt.Amount.AmountMinor,
                                        currency = attempt.Amount.CurrencyCode,
                                        paid_at = unixNow,
                                        failed_code = command.FailureCode,
                                        failed_message = command.FailureMessage
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        string payloadJson = JsonSerializer.Serialize(payloadObj);

        string secret = _secretProvider.GetWebhookSigningKey();
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = "whsec_simulation_key_001";
        }

        string signatureData = $"{unixNow}.{payloadJson}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureData));
        string testSig = Convert.ToHexStringLower(hash);
        string signatureHeader = $"t={unixNow},te={testSig}";

        var webhookEvent = WebhookEvent.Create(
            providerEventId: simulatedEventId,
            eventType: command.EventType,
            payloadJson: payloadJson,
            processingStatus: WebhookProcessingStatus.Processed,
            nowUtc: nowUtc);

        await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);

        string outcomeResult;
        if (command.EventType is "checkout_session.payment.paid" or "payment.paid")
        {
            attempt.MarkSucceeded(nowUtc, attempt.PaymentMethod);
            if (order is not null && order.Status != OrderStatus.Paid)
            {
                order.MarkPaid(nowUtc);
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
                    paidAtUtc: nowUtc);

                await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
            }
            outcomeResult = "payment_succeeded_outbox_queued";
        }
        else if (command.EventType is "checkout_session.expired")
        {
            attempt.MarkExpired(nowUtc);
            outcomeResult = "session_expired";
        }
        else
        {
            attempt.MarkFailed(command.FailureCode ?? "SIMULATED_FAILURE", command.FailureMessage ?? "Simulation failure", nowUtc);
            outcomeResult = "payment_failed";
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        return Result.Success(new SimulateWebhookResponse(
            Success: true,
            SimulatedEventId: simulatedEventId,
            EventType: command.EventType,
            SignatureHeader: signatureHeader,
            PayloadJson: payloadJson,
            Result: outcomeResult));
    }
}
