using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Application.Ports;

public sealed record CreateCheckoutSessionRequest(
    string OrderId,
    string OrderNumber,
    string Description,
    Money Amount,
    string RequestedPaymentMethod,
    string IdempotencyKey,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    IReadOnlyList<CheckoutLineItemDto> LineItems);

public sealed record CheckoutLineItemDto(
    string Name,
    int Quantity,
    long AmountMinor,
    string Currency = "PHP",
    string? Description = null);

public sealed record CheckoutSessionResult(
    string ProviderPaymentId,
    string CheckoutUrl,
    string Status,
    DateTimeOffset? ExpiresAtUtc);

public sealed record GatewayPaymentStatusResult(
    string ProviderPaymentId,
    string Status,
    string? PaymentMethodUsed,
    DateTimeOffset? PaidAtUtc,
    string? FailureCode,
    string? FailureMessage);

/// <summary>
/// Port interface for interacting with the payment gateway (PayMongo v1 API).
/// </summary>
public interface IPaymentGateway
{
    Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<GatewayPaymentStatusResult>> GetCheckoutSessionStatusAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default);

    Task<Result> ExpireCheckoutSessionAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ProbeConnectionAsync(
        CancellationToken cancellationToken = default);
}

