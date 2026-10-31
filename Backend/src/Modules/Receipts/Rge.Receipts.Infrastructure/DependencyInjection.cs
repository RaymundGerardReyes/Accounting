using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.GetReceiptByOrder;
using Rge.Receipts.Application.GetReceiptByPayment;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.RenderReceiptPdf;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Contracts;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Digest;
using Rge.Receipts.Infrastructure.Mail;
using Rge.Receipts.Infrastructure.Pdf;
using Rge.Receipts.Infrastructure.Persistence;

namespace Rge.Receipts.Infrastructure;

/// <summary>
/// Cross-module adapter implementing IReceiptLookup so Payments and Integrations (Google Sheets)
/// can query issued BIR Official Receipts without coupling to Rge.Receipts internals.
/// </summary>
public sealed class ReceiptLookupAdapter : IReceiptLookup
{
    private readonly IReceiptRepository _repository;

    public ReceiptLookupAdapter(IReceiptRepository repository)
    {
        _repository = repository;
    }

    public async Task<ReceiptSummaryContract?> FindByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        OfficialReceipt? receipt = await _repository.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        return receipt is null ? null : MapToContract(receipt);
    }

    public async Task<ReceiptSummaryContract?> FindByPaymentAttemptIdAsync(
        string paymentAttemptId,
        CancellationToken cancellationToken = default)
    {
        OfficialReceipt? receipt = await _repository.GetByPaymentAttemptIdAsync(paymentAttemptId, cancellationToken).ConfigureAwait(false);
        return receipt is null ? null : MapToContract(receipt);
    }

    private static ReceiptSummaryContract MapToContract(OfficialReceipt receipt) =>
        new(
            ReceiptId: receipt.Id,
            OrderId: receipt.OrderId,
            OrderNumber: receipt.OrderNumber,
            PaymentAttemptId: receipt.PaymentAttemptId,
            OfficialReceiptNumber: receipt.ReceiptNumber.Value,
            TreasuryAcknowledgementNumber: receipt.Acknowledgement.AcknowledgementNumber,
            GrossAmountMinor: receipt.VatBreakdown.GrossAmount.AmountMinor,
            Currency: receipt.VatBreakdown.GrossAmount.CurrencyCode,
            AuditDigest: receipt.Digest.Value,
            IssuedAtUtc: receipt.IssuedAtUtc);
}

/// <summary>
/// Outbox handler that atomically issues a BIR Official Receipt, pre-renders the PDF into cache,
/// and dispatches the customer email off the webhook request path when 'order.paid' is processed.
/// </summary>
public sealed class OrderPaidReceiptOutboxHandler : IOutboxEventHandler
{
    private readonly IssueReceiptHandler _issueReceiptHandler;
    private readonly RenderReceiptPdfHandler _renderPdfHandler;
    private readonly ResendReceiptEmailHandler _emailHandler;

    public string EventType => "order.paid";

    public OrderPaidReceiptOutboxHandler(
        IssueReceiptHandler issueReceiptHandler,
        RenderReceiptPdfHandler renderPdfHandler,
        ResendReceiptEmailHandler emailHandler)
    {
        ArgumentNullException.ThrowIfNull(issueReceiptHandler);
        ArgumentNullException.ThrowIfNull(renderPdfHandler);
        ArgumentNullException.ThrowIfNull(emailHandler);

        _issueReceiptHandler = issueReceiptHandler;
        _renderPdfHandler = renderPdfHandler;
        _emailHandler = emailHandler;
    }

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string? orderId = message.OrderId;
        string? paymentAttemptId = message.PaymentAttemptId;
        long amountMinor = 0;
        string currency = "PHP";
        string? orderNumber = null;
        string customerName = "Valued Customer";
        string customerEmail = string.Empty;
        string? description = null;
        string paymentMethod = "paymongo";

        if (!string.IsNullOrWhiteSpace(message.PayloadJson))
        {
            using JsonDocument doc = JsonDocument.Parse(message.PayloadJson);
            JsonElement root = doc.RootElement;

            if (string.IsNullOrWhiteSpace(orderId))
            {
                orderId = TryGetString(root, "order_id", "OrderId");
            }

            if (string.IsNullOrWhiteSpace(paymentAttemptId))
            {
                paymentAttemptId = TryGetString(root, "payment_attempt_id", "PaymentAttemptId");
            }

            if (TryGetInt64(root, out long parsedAmt, "amount_minor", "AmountMinor"))
            {
                amountMinor = parsedAmt;
            }

            if (TryGetString(root, "currency", "Currency") is { Length: > 0 } currStr)
            {
                currency = currStr;
            }

            if (TryGetString(root, "order_number", "OrderNumber") is { Length: > 0 } ordStr)
            {
                orderNumber = ordStr;
            }

            if (TryGetString(root, "customer_name", "CustomerName") is { Length: > 0 } nameStr)
            {
                customerName = nameStr;
            }

            if (TryGetString(root, "customer_email", "CustomerEmail") is { Length: > 0 } emailStr)
            {
                customerEmail = emailStr;
            }

            if (TryGetString(root, "description", "Description") is { Length: > 0 } descStr)
            {
                description = descStr;
            }

            if (TryGetString(root, "payment_method", "PaymentMethod") is { Length: > 0 } pmStr)
            {
                paymentMethod = pmStr;
            }
        }

        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentAttemptId) || amountMinor <= 0)
        {
            throw new InvalidOperationException(
                $"Outbox message '{message.Id}' is missing required order_id, payment_attempt_id, or positive amount_minor.");
        }

        orderNumber ??= $"ORD-{orderId[..Math.Min(8, orderId.Length)].ToUpperInvariant()}";
        description ??= $"Payment for {orderNumber}";

        var issueCommand = new IssueReceiptCommand(
            OrderId: orderId,
            OrderNumber: orderNumber,
            PaymentAttemptId: paymentAttemptId,
            AmountMinor: amountMinor,
            Currency: currency,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            Description: description,
            PaymentMethod: paymentMethod);

        var issueResult = await _issueReceiptHandler
            .HandleAsync(issueCommand, cancellationToken)
            .ConfigureAwait(false);

        if (issueResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to issue BIR Official Receipt for Order '{orderId}': {issueResult.Error.Code} - {issueResult.Error.Message}");
        }

        // Pre-warm the PDF into the in-memory cache off the request path so downloads stream in < 2 ms
        await _renderPdfHandler
            .HandleAsync(new RenderReceiptPdfQuery(PaymentId: paymentAttemptId), cancellationToken)
            .ConfigureAwait(false);

        // If a customer email is present, dispatch the receipt email asynchronously
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            await _emailHandler
                .HandleAsync(new ResendReceiptEmailCommand(paymentAttemptId, customerEmail), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string? TryGetString(JsonElement root, string snakeName, string pascalName)
    {
        if (root.TryGetProperty(snakeName, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }
        if (root.TryGetProperty(pascalName, out JsonElement elPascal) && elPascal.ValueKind == JsonValueKind.String)
        {
            return elPascal.GetString();
        }
        return null;
    }

    private static bool TryGetInt64(JsonElement root, out long value, string snakeName, string pascalName)
    {
        if (root.TryGetProperty(snakeName, out JsonElement el) && el.TryGetInt64(out value))
        {
            return true;
        }
        if (root.TryGetProperty(pascalName, out JsonElement elPascal) && elPascal.TryGetInt64(out value))
        {
            return true;
        }
        value = 0;
        return false;
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
        services.AddScoped<IReceiptLookup, ReceiptLookupAdapter>();

        services.AddScoped<IssueReceiptHandler>();
        services.AddScoped<GetReceiptByPaymentHandler>();
        services.AddScoped<GetReceiptByOrderHandler>();
        services.AddScoped<RenderReceiptPdfHandler>();
        services.AddScoped<ResendReceiptEmailHandler>();
        services.AddScoped<OrderPaidReceiptOutboxHandler>();

        return services;
    }
}
