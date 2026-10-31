using System.Text.Json;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Application.Mapping;

public sealed record PaymentAttemptDto(
    string Id,
    string OrderId,
    string Provider,
    string PaymentMethod,
    string? ProviderPaymentId,
    long AmountMinor,
    string Currency,
    string Status,
    string IdempotencyKey,
    string? CheckoutUrl,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OrderDto(
    string Id,
    string UserId,
    string OrderNumber,
    string Title,
    string Category,
    string ReferenceCode,
    long AmountMinor,
    string Currency,
    string Status,
    IReadOnlyDictionary<string, object?> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    PaymentAttemptDto? LatestAttempt = null,
    IReadOnlyList<PaymentAttemptDto>? Attempts = null);

public sealed record WebhookEventDto(
    string Id,
    string Provider,
    string ProviderEventId,
    string EventType,
    string PayloadJson,
    string ProcessingStatus,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? ErrorMessage);

public static class PaymentMappings
{
    public static PaymentAttemptDto ToDto(this PaymentAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new PaymentAttemptDto(
            Id: attempt.Id,
            OrderId: attempt.OrderId,
            Provider: attempt.Provider,
            PaymentMethod: attempt.PaymentMethod,
            ProviderPaymentId: attempt.ProviderPaymentId,
            AmountMinor: attempt.Amount.AmountMinor,
            Currency: attempt.Amount.CurrencyCode,
            Status: attempt.Status.ToString().ToLowerInvariant(),
            IdempotencyKey: attempt.IdempotencyKey,
            CheckoutUrl: attempt.CheckoutUrl,
            FailureCode: attempt.FailureCode,
            FailureMessage: attempt.FailureMessage,
            ExpiresAt: attempt.ExpiresAt,
            PaidAt: attempt.PaidAt,
            CreatedAt: attempt.CreatedAt,
            UpdatedAt: attempt.UpdatedAt);
    }

    public static OrderDto ToDto(
        this Order order,
        PaymentAttemptDto? latestAttempt = null,
        IReadOnlyList<PaymentAttemptDto>? attempts = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        IReadOnlyDictionary<string, object?> metadataDict;
        try
        {
            metadataDict = string.IsNullOrWhiteSpace(order.MetadataJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(order.MetadataJson)
                  ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            metadataDict = new Dictionary<string, object?>();
        }

        return new OrderDto(
            Id: order.Id,
            UserId: order.UserId,
            OrderNumber: order.OrderNumber,
            Title: order.Title,
            Category: order.Category,
            ReferenceCode: order.ReferenceCode,
            AmountMinor: order.Amount.AmountMinor,
            Currency: order.Amount.CurrencyCode,
            Status: order.Status,
            Metadata: metadataDict,
            CreatedAt: order.CreatedAt,
            UpdatedAt: order.UpdatedAt,
            LatestAttempt: latestAttempt,
            Attempts: attempts);
    }

    public static WebhookEventDto ToDto(this WebhookEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return new WebhookEventDto(
            Id: evt.Id,
            Provider: evt.Provider,
            ProviderEventId: evt.ProviderEventId,
            EventType: evt.EventType,
            PayloadJson: evt.PayloadJson,
            ProcessingStatus: evt.ProcessingStatus,
            ReceivedAt: evt.ReceivedAt,
            ProcessedAt: evt.ProcessedAt,
            ErrorMessage: evt.ErrorMessage);
    }
}

