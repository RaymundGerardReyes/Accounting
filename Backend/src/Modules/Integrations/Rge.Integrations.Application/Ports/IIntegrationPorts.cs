// File: Backend/src/Modules/Integrations/Rge.Integrations.Application/Ports/IIntegrationPorts.cs
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Application.Ports;

public sealed record BridgedGoogleFormOrderResult(
    string OrderId,
    string OrderNumber,
    string OrderTitle,
    long AmountMinor,
    string AmountPhp,
    string PaymentAttemptId,
    string? ProviderPaymentId,
    string CheckoutUrl,
    string Status);

public sealed record BridgedGoogleFormStatusResult(
    string OrderId,
    string OrderNumber,
    string OrderStatus,
    string? PaymentAttemptId,
    string? ProviderPaymentId,
    string PaymentStatus,
    string? CheckoutUrl,
    long AmountMinor,
    string? OfficialReceiptNumber,
    string? TreasuryAcknowledgementNumber,
    DateTimeOffset? PaidAtUtc);

public interface IGoogleFormOrderBridge
{
    Task<Result<BridgedGoogleFormOrderResult>> CreateOrderAndCheckoutSessionAsync(
        string responseId,
        int sheetRowIndex,
        string customerName,
        string customerEmail,
        string? customerPhone,
        string itemReference,
        string category,
        int quantity,
        string paymentMethod,
        string? notes,
        CancellationToken cancellationToken = default);

    Task<BridgedGoogleFormStatusResult?> ResolveLiveStatusAsync(
        string orderId,
        string? paymentAttemptId,
        CancellationToken cancellationToken = default);
}