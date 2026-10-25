using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Payments.Application.Sandbox.SimulateWebhook;

public sealed record SimulateWebhookCommand(
    string PaymentAttemptId,
    string EventType = "checkout_session.payment.paid",
    string? FailureCode = null,
    string? FailureMessage = null) : ICommand<SimulateWebhookResponse>;

public sealed record SimulateWebhookResponse(
    bool Success,
    string SimulatedEventId,
    string EventType,
    string SignatureHeader,
    string PayloadJson,
    string Result);
