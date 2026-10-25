using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Represents an individual PayMongo checkout session initiated for an Order.
/// </summary>
public sealed class PaymentAttempt : Entity<string>
{
    public string OrderId { get; private set; }
    public string Provider { get; private set; }
    public string PaymentMethod { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PaymentAttempt(
        string id,
        string orderId,
        string provider,
        string paymentMethod,
        string? providerPaymentId,
        Money amount,
        PaymentAttemptStatus status,
        string idempotencyKey,
        string? checkoutUrl,
        DateTimeOffset? expiresAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) : base(id)
    {
        OrderId = Guard.AgainstNullOrWhiteSpace(orderId);
        Provider = Guard.AgainstNullOrWhiteSpace(provider);
        PaymentMethod = Guard.AgainstNullOrWhiteSpace(paymentMethod);
        ProviderPaymentId = providerPaymentId;
        Guard.AgainstNonPositive(amount.AmountMinor, nameof(amount));
        Amount = amount;
        Status = status;
        IdempotencyKey = Guard.AgainstNullOrWhiteSpace(idempotencyKey);
        CheckoutUrl = checkoutUrl;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static PaymentAttempt CreatePending(
        string orderId,
        string paymentMethod,
        Money amount,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        string provider = "paymongo")
    {
        return new PaymentAttempt(
            id: Guid.NewGuid().ToString("D"),
            orderId: orderId,
            provider: provider,
            paymentMethod: paymentMethod,
            providerPaymentId: null,
            amount: amount,
            status: PaymentAttemptStatus.Pending,
            idempotencyKey: idempotencyKey,
            checkoutUrl: null,
            expiresAt: null,
            createdAt: nowUtc,
            updatedAt: nowUtc);
    }

    public static PaymentAttempt Rehydrate(
        string id,
        string orderId,
        string provider,
        string paymentMethod,
        string? providerPaymentId,
        Money amount,
        PaymentAttemptStatus status,
        string idempotencyKey,
        string? checkoutUrl,
        string? failureCode,
        string? failureMessage,
        DateTimeOffset? expiresAt,
        DateTimeOffset? paidAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var attempt = new PaymentAttempt(
            id, orderId, provider, paymentMethod, providerPaymentId, amount,
            status, idempotencyKey, checkoutUrl, expiresAt, createdAt, updatedAt);
        attempt.FailureCode = failureCode;
        attempt.FailureMessage = failureMessage;
        attempt.PaidAt = paidAt;
        return attempt;
    }

    public Result AttachCheckoutSession(
        string providerPaymentId,
        string checkoutUrl,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(providerPaymentId);
        Guard.AgainstNullOrWhiteSpace(checkoutUrl);

        var transition = PaymentAttemptStateMachine.Transition(Id, Status, PaymentAttemptStatus.RequiresAction);
        if (transition.IsFailure)
        {
            return transition;
        }

        ProviderPaymentId = providerPaymentId;
        CheckoutUrl = checkoutUrl;
        ExpiresAt = expiresAtUtc;
        Status = transition.Value;
        UpdatedAt = nowUtc;

        return Result.Success();
    }

    public Result MarkSucceeded(DateTimeOffset paidAtUtc, string? actualPaymentMethod = null)
    {
        var transition = PaymentAttemptStateMachine.Transition(Id, Status, PaymentAttemptStatus.Succeeded);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = transition.Value;
        PaidAt = paidAtUtc;
        UpdatedAt = paidAtUtc;

        if (!string.IsNullOrWhiteSpace(actualPaymentMethod))
        {
            PaymentMethod = actualPaymentMethod;
        }

        return Result.Success();
    }

    public Result MarkFailed(string failureCode, string failureMessage, DateTimeOffset nowUtc)
    {
        var transition = PaymentAttemptStateMachine.Transition(Id, Status, PaymentAttemptStatus.Failed);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = transition.Value;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = nowUtc;

        return Result.Success();
    }

    public Result MarkExpired(DateTimeOffset nowUtc)
    {
        var transition = PaymentAttemptStateMachine.Transition(Id, Status, PaymentAttemptStatus.Expired);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = transition.Value;
        FailureCode = "SESSION_EXPIRED";
        FailureMessage = "Checkout session expired before payment completion.";
        UpdatedAt = nowUtc;

        return Result.Success();
    }
}

