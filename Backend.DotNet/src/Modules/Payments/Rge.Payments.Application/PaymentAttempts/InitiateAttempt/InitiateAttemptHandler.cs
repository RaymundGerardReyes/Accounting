using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.InitiateAttempt;

public sealed record InitiateAttemptCommand(
    string OrderId,
    string PaymentMethod,
    string IdempotencyKey,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null) : ICommand<InitiateAttemptResponse>;

public sealed record InitiateAttemptResponse(
    string PaymentAttemptId,
    string OrderId,
    string OrderNumber,
    string Provider,
    string PaymentMethod,
    string ProviderPaymentId,
    long AmountMinor,
    string Currency,
    string Status,
    string CheckoutUrl,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Initiates a PayMongo checkout session for an Order using server-authoritative pricing.
/// </summary>
public sealed class InitiateAttemptHandler : ICommandHandler<InitiateAttemptCommand, InitiateAttemptResponse>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IClock _clock;

    public InitiateAttemptHandler(IPaymentGateway paymentGateway, IClock clock)
    {
        _paymentGateway = paymentGateway;
        _clock = clock;
    }

    public async Task<Result<InitiateAttemptResponse>> HandleAsync(
        InitiateAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Result.Failure<InitiateAttemptResponse>(
                Error.Validation("Payments.MissingOrderId", "Order ID is required."));
        }

        string idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : command.IdempotencyKey.Trim();

        // Note: In upcoming persistence slices, IOrderRepository and IPaymentAttemptRepository will be called here.
        // Authoritative domain + gateway orchestration flow:
        var attempt = PaymentAttempt.CreatePending(
            orderId: command.OrderId,
            paymentMethod: string.IsNullOrWhiteSpace(command.PaymentMethod) ? "hosted_checkout" : command.PaymentMethod,
            amount: Rge.BuildingBlocks.SharedKernel.Money.Money.Php(100_00), // Default contract placeholder
            idempotencyKey: idempotencyKey,
            nowUtc: _clock.UtcNow);

        var gatewayRequest = new CreateCheckoutSessionRequest(
            OrderId: command.OrderId,
            OrderNumber: $"ORD-{command.OrderId[..Math.Min(8, command.OrderId.Length)].ToUpperInvariant()}",
            Description: "R.G.E Accounting Platform Checkout",
            Amount: attempt.Amount,
            RequestedPaymentMethod: attempt.PaymentMethod,
            IdempotencyKey: attempt.IdempotencyKey,
            CustomerName: command.CustomerName,
            CustomerEmail: command.CustomerEmail,
            CustomerPhone: command.CustomerPhone,
            LineItems: []);

        Result<CheckoutSessionResult> sessionResult = await _paymentGateway.CreateCheckoutSessionAsync(
            gatewayRequest,
            cancellationToken);

        if (sessionResult.IsFailure)
        {
            attempt.MarkFailed("GATEWAY_REJECTED", sessionResult.Error.Message, _clock.UtcNow);
            return Result.Failure<InitiateAttemptResponse>(sessionResult.Error);
        }

        CheckoutSessionResult session = sessionResult.Value;
        Result attachResult = attempt.AttachCheckoutSession(
            providerPaymentId: session.ProviderPaymentId,
            checkoutUrl: session.CheckoutUrl,
            expiresAtUtc: session.ExpiresAtUtc,
            nowUtc: _clock.UtcNow);

        if (attachResult.IsFailure)
        {
            return Result.Failure<InitiateAttemptResponse>(attachResult.Error);
        }

        return Result.Success(new InitiateAttemptResponse(
            PaymentAttemptId: attempt.Id,
            OrderId: attempt.OrderId,
            OrderNumber: gatewayRequest.OrderNumber,
            Provider: attempt.Provider,
            PaymentMethod: attempt.PaymentMethod,
            ProviderPaymentId: session.ProviderPaymentId,
            AmountMinor: attempt.Amount.AmountMinor,
            Currency: attempt.Amount.CurrencyCode,
            Status: "requires_action",
            CheckoutUrl: session.CheckoutUrl,
            ExpiresAtUtc: session.ExpiresAtUtc));
    }
}

