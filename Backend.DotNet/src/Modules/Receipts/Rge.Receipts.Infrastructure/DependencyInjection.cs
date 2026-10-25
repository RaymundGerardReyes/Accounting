using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.Receipts.Application.GetReceiptByOrder;
using Rge.Receipts.Application.GetReceiptByPayment;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.RenderReceiptPdf;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Infrastructure.Digest;
using Rge.Receipts.Infrastructure.Mail;
using Rge.Receipts.Infrastructure.Pdf;
using Rge.Receipts.Infrastructure.Persistence;

namespace Rge.Receipts.Infrastructure;

/// <summary>
/// Outbox handler that automatically issues a BIR Official Receipt when an 'order.paid' event is dispatched.
/// </summary>
public sealed class OrderPaidReceiptOutboxHandler : IOutboxEventHandler
{
    private readonly IssueReceiptHandler _issueReceiptHandler;

    public string EventType => "order.paid";

    public OrderPaidReceiptOutboxHandler(IssueReceiptHandler issueReceiptHandler)
    {
        ArgumentNullException.ThrowIfNull(issueReceiptHandler);
        _issueReceiptHandler = issueReceiptHandler;
    }

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string orderId = message.OrderId ?? Guid.NewGuid().ToString("D");
        string paymentAttemptId = message.PaymentAttemptId ?? Guid.NewGuid().ToString("D");
        long amountMinor = 100_00;
        string orderNumber = $"ORD-{orderId[..Math.Min(8, orderId.Length)].ToUpperInvariant()}";

        if (!string.IsNullOrWhiteSpace(message.PayloadJson))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(message.PayloadJson);
                if (doc.RootElement.TryGetProperty("amount_minor", out JsonElement amtEl) && amtEl.TryGetInt64(out long parsedAmt))
                {
                    amountMinor = parsedAmt;
                }
                if (doc.RootElement.TryGetProperty("order_number", out JsonElement ordEl) && ordEl.GetString() is string ordStr)
                {
                    orderNumber = ordStr;
                }
            }
            catch (JsonException)
            {
            }
        }

        var cmd = new IssueReceiptCommand(
            OrderId: orderId,
            OrderNumber: orderNumber,
            PaymentAttemptId: paymentAttemptId,
            AmountMinor: amountMinor,
            Currency: "PHP",
            CustomerName: "Valued Customer",
            CustomerEmail: string.Empty,
            Description: $"Payment for {orderNumber}",
            PaymentMethod: "paymongo");

        await _issueReceiptHandler.HandleAsync(cmd, cancellationToken).ConfigureAwait(false);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddReceiptsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICanonicalJsonSerializer, CanonicalJsonSerializer>();
        services.AddSingleton<IPdfRenderer, QuestPdfReceiptRenderer>();
        services.AddSingleton<SmtpReceiptOptions>();
        services.AddScoped<IReceiptMailer, SmtpReceiptMailer>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();

        services.AddScoped<IssueReceiptHandler>();
        services.AddScoped<GetReceiptByPaymentHandler>();
        services.AddScoped<GetReceiptByOrderHandler>();
        services.AddScoped<RenderReceiptPdfHandler>();
        services.AddScoped<ResendReceiptEmailHandler>();
        services.AddScoped<OrderPaidReceiptOutboxHandler>();

        return services;
    }
}
