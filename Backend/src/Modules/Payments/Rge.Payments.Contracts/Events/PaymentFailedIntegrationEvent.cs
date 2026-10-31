using Rge.BuildingBlocks.Application.Messaging;

namespace Rge.Payments.Contracts.Events;

/// <summary>
/// Cross-module integration event published when a payment attempt fails or expires.
/// </summary>
public sealed record PaymentFailedIntegrationEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string OrderId,
    string PaymentAttemptId,
    string FailureCode,
    string FailureMessage) : IIntegrationEvent
{
    public const string Type = "payment.failed";

    public static PaymentFailedIntegrationEvent Create(
        string orderId,
        string paymentAttemptId,
        string failureCode,
        string failureMessage,
        DateTimeOffset occurredAtUtc)
    {
        return new PaymentFailedIntegrationEvent(
            EventId: Guid.NewGuid(),
            EventType: Type,
            OccurredAtUtc: occurredAtUtc,
            CorrelationId: paymentAttemptId,
            OrderId: orderId,
            PaymentAttemptId: paymentAttemptId,
            FailureCode: failureCode,
            FailureMessage: failureMessage);
    }
}

