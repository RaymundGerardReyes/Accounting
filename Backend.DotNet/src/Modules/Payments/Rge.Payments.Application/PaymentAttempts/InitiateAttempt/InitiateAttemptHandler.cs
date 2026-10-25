using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.InitiateAttempt;

public sealed class InitiateAttemptHandler : ICommandHandler<InitiateAttemptCommand, PaymentAttemptDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IClock _clock;

    public InitiateAttemptHandler(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository,
        IPaymentGateway paymentGateway,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(clock);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
        _paymentGateway = paymentGateway;
        _clock = clock;
    }

    public async Task<Result<PaymentAttemptDto>> HandleAsync(
        InitiateAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Result.Failure<PaymentAttemptDto>(Error.Validation("InitiateAttempt.OrderIdRequired", "Order ID is required."));
        }

        Order? order = await _orderRepository.GetByIdAsync(command.OrderId.Trim(), cancellationToken).ConfigureAwait(false)
            ?? await _orderRepository.GetByOrderNumberAsync(command.OrderId.Trim(), cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<PaymentAttemptDto>(OrderErrors.NotFound(command.OrderId));
        }

        if (order.Status == OrderStatus.Paid)
        {
            return Result.Failure<PaymentAttemptDto>(OrderErrors.AlreadyPaid(order.OrderNumber));
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Result.Failure<PaymentAttemptDto>(OrderErrors.AlreadyCancelled(order.OrderNumber));
        }

        string rawIdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? Guid.NewGuid().ToString("D")
            : command.IdempotencyKey.Trim();

        PaymentAttempt? existing = await _attemptRepository
            .GetByIdempotencyKeyAsync(rawIdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.OrderId != order.Id)
            {
                return Result.Failure<PaymentAttemptDto>(
                    Error.Conflict("Payments.IdempotencyConflict", $"Idempotency key '{rawIdempotencyKey}' was used for another order."));
            }

            return Result.Success(existing.ToDto());
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string normalizedMethod = Domain.PaymentAttempts.PaymentMethod.Normalize(command.PaymentMethod);

        // Update customer details in Order metadata if provided
        if (!string.IsNullOrWhiteSpace(command.CustomerName) ||
            !string.IsNullOrWhiteSpace(command.CustomerEmail) ||
            !string.IsNullOrWhiteSpace(command.CustomerPhone))
        {
            try
            {
                var meta = string.IsNullOrWhiteSpace(order.MetadataJson)
                    ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(order.MetadataJson)
                      ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(command.CustomerName))
                {
                    meta["customer_name"] = command.CustomerName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(command.CustomerEmail))
                {
                    meta["customer_email"] = command.CustomerEmail.Trim();
                }
                if (!string.IsNullOrWhiteSpace(command.CustomerPhone))
                {
                    meta["customer_phone"] = command.CustomerPhone.Trim();
                }

                order = Order.Create(
                    orderNumber: order.OrderNumber,
                    title: order.Title,
                    category: order.Category,
                    referenceCode: order.ReferenceCode,
                    authoritativeAmount: order.Amount,
                    metadataJson: JsonSerializer.Serialize(meta),
                    nowUtc: nowUtc,
                    userId: order.UserId);

                await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
            }
        }

        var attempt = PaymentAttempt.CreatePending(
            orderId: order.Id,
            paymentMethod: normalizedMethod,
            amount: order.Amount,
            idempotencyKey: rawIdempotencyKey,
            nowUtc: nowUtc,
            provider: "paymongo");

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        string? custName = command.CustomerName;
        string? custEmail = command.CustomerEmail;
        string? custPhone = command.CustomerPhone;
        if (string.IsNullOrWhiteSpace(custName) || string.IsNullOrWhiteSpace(custEmail))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(order.MetadataJson);
                if (string.IsNullOrWhiteSpace(custName) && doc.RootElement.TryGetProperty("customer_name", out JsonElement cn))
                {
                    custName = cn.GetString();
                }
                if (string.IsNullOrWhiteSpace(custEmail) && doc.RootElement.TryGetProperty("customer_email", out JsonElement ce))
                {
                    custEmail = ce.GetString();
                }
                if (string.IsNullOrWhiteSpace(custPhone) && doc.RootElement.TryGetProperty("customer_phone", out JsonElement cp))
                {
                    custPhone = cp.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }

        var checkoutReq = new CreateCheckoutSessionRequest(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Description: order.Title,
            Amount: order.Amount,
            RequestedPaymentMethod: normalizedMethod,
            IdempotencyKey: rawIdempotencyKey,
            CustomerName: custName,
            CustomerEmail: custEmail,
            CustomerPhone: custPhone,
            LineItems: [new CheckoutLineItemDto(order.Title, 1, order.Amount.AmountMinor, order.Amount.CurrencyCode)]);

        Result<CheckoutSessionResult> gatewayResult = await _paymentGateway
            .CreateCheckoutSessionAsync(checkoutReq, cancellationToken)
            .ConfigureAwait(false);

        if (gatewayResult.IsFailure)
        {
            attempt.MarkFailed("GATEWAY_ERROR", gatewayResult.Error.Message, nowUtc);
            await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
            return Result.Failure<PaymentAttemptDto>(gatewayResult.Error);
        }

        attempt.AttachCheckoutSession(
            providerPaymentId: gatewayResult.Value.ProviderPaymentId,
            checkoutUrl: gatewayResult.Value.CheckoutUrl,
            expiresAtUtc: gatewayResult.Value.ExpiresAtUtc,
            nowUtc: nowUtc);

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        return Result.Success(attempt.ToDto());
    }
}
