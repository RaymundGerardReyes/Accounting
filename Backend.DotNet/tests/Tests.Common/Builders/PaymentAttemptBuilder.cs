using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Tests.Common.Builders;

/// <summary>
/// Fluent test builder for PaymentAttempt aggregate.
/// </summary>
public sealed class PaymentAttemptBuilder
{
    private string _orderId = "ord-test-default";
    private string _paymentMethod = "qrph";
    private Money _amount = Money.Php(150_00);
    private string _idempotencyKey = "idem-default";
    private DateTimeOffset _nowUtc = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private string _provider = "paymongo";

    public PaymentAttemptBuilder WithOrderId(string orderId)
    {
        _orderId = orderId;
        return this;
    }

    public PaymentAttemptBuilder WithPaymentMethod(string paymentMethod)
    {
        _paymentMethod = paymentMethod;
        return this;
    }

    public PaymentAttemptBuilder WithAmount(Money amount)
    {
        _amount = amount;
        return this;
    }

    public PaymentAttemptBuilder WithIdempotencyKey(string idempotencyKey)
    {
        _idempotencyKey = idempotencyKey;
        return this;
    }

    public PaymentAttemptBuilder WithTimestamp(DateTimeOffset nowUtc)
    {
        _nowUtc = nowUtc;
        return this;
    }

    public PaymentAttempt BuildPending() =>
        PaymentAttempt.CreatePending(_orderId, _paymentMethod, _amount, _idempotencyKey, _nowUtc, _provider);

    public PaymentAttempt BuildRequiresAction(string providerPaymentId = "cs_test_123", string checkoutUrl = "https://checkout.paymongo.com/cs_test_123")
    {
        var attempt = BuildPending();
        attempt.AttachCheckoutSession(providerPaymentId, checkoutUrl, _nowUtc.AddHours(1), _nowUtc);
        return attempt;
    }

    public PaymentAttempt BuildSucceeded(string providerPaymentId = "cs_test_123")
    {
        var attempt = BuildRequiresAction(providerPaymentId);
        attempt.MarkSucceeded(_nowUtc.AddMinutes(5), _paymentMethod);
        return attempt;
    }
}

