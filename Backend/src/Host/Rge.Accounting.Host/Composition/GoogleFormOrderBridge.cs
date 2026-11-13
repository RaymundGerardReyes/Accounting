// File: Backend/src/Host/Rge.Accounting.Host/Composition/GoogleFormOrderBridge.cs
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Domain.Pricing;
using Rge.Receipts.Contracts;

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Cross-module bridge that creates real PostgreSQL/in-memory Orders and live PayMongo
/// Checkout Sessions when a Google Form is submitted, and resolves live payment/receipt status.
/// </summary>
public sealed class GoogleFormOrderBridge : IGoogleFormOrderBridge
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IPricingPolicy _pricingPolicy;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReceiptLookup _receiptLookup;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public GoogleFormOrderBridge(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository,
        IPricingPolicy pricingPolicy,
        IPaymentGateway paymentGateway,
        IReceiptLookup receiptLookup,
        IClock clock,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(pricingPolicy);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(receiptLookup);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(configuration);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
        _pricingPolicy = pricingPolicy;
        _paymentGateway = paymentGateway;
        _receiptLookup = receiptLookup;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<Result<BridgedGoogleFormOrderResult>> CreateOrderAndCheckoutSessionAsync(
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
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = _clock.UtcNow;
        string normalizedCategory = string.Equals(category, OrderCategory.DepartmentInvoice, StringComparison.OrdinalIgnoreCase)
            ? OrderCategory.DepartmentInvoice
            : OrderCategory.ProductOrder;

        string cleanReference = string.IsNullOrWhiteSpace(itemReference) ? "Product A" : itemReference.Trim();
        int safeQty = Math.Max(1, quantity);

        Result<Money> priceResult = _pricingPolicy.CalculateAuthoritativeAmount(
            normalizedCategory,
            cleanReference,
            safeQty);

        if (priceResult.IsFailure)
        {
            return Result.Failure<BridgedGoogleFormOrderResult>(priceResult.Error);
        }

        Money totalAmount = priceResult.Value;
        string prefix = normalizedCategory == OrderCategory.ProductOrder ? "ORD" : "INV";
        string yearMonth = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        long seq = await _orderRepository.NextOrderSequenceAsync(prefix, yearMonth, cancellationToken).ConfigureAwait(false);
        OrderNumber orderNumber = OrderNumber.Create(normalizedCategory, nowUtc, seq);

        string orderTitle = safeQty > 1
            ? $"{cleanReference} (x{safeQty})"
            : cleanReference;

        var metadataObj = new
        {
            source = "google_forms",
            google_form_response_id = responseId,
            sheet_row_index = sheetRowIndex,
            customer_name = customerName,
            customer_email = customerEmail,
            customer_phone = customerPhone ?? string.Empty,
            quantity = safeQty,
            notes = notes ?? string.Empty
        };

        string metadataJson = JsonSerializer.Serialize(metadataObj);

        Order order = Order.Create(
            orderNumber: orderNumber.Value,
            title: orderTitle,
            category: normalizedCategory,
            referenceCode: cleanReference,
            authoritativeAmount: totalAmount,
            metadataJson: metadataJson,
            nowUtc: nowUtc,
            userId: "google_forms_bridge");

        await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

        string normalizedMethod = PaymentMethod.Normalize(paymentMethod);
        string idempotencyKey = $"gf_attempt_{responseId}";

        PaymentAttempt attempt = PaymentAttempt.CreatePending(
            orderId: order.Id,
            paymentMethod: normalizedMethod,
            amount: totalAmount,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        long unitAmountMinor = totalAmount.AmountMinor / safeQty;
        var lineItems = new List<CheckoutLineItemRequest>
        {
            new(
                Name: cleanReference,
                Quantity: safeQty,
                AmountMinor: unitAmountMinor,
                Currency: totalAmount.CurrencyCode,
                Description: string.IsNullOrWhiteSpace(notes) ? orderTitle : notes)
        };

        var gatewayRequest = new CreateCheckoutSessionRequest(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Description: $"{order.OrderNumber} - {orderTitle}",
            Amount: totalAmount,
            RequestedPaymentMethod: normalizedMethod,
            IdempotencyKey: idempotencyKey,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            CustomerPhone: customerPhone,
            LineItems: lineItems,
            SuccessUrl: null,
            CancelUrl: null,
            PaymentAttemptId: attempt.Id);

        Result<CheckoutSessionResult> sessionResult = await _paymentGateway
            .CreateCheckoutSessionAsync(gatewayRequest, cancellationToken)
            .ConfigureAwait(false);

        string appBaseUrl = (_configuration["NEXT_PUBLIC_APP_URL"]
            ?? _configuration["APP_URL"]
            ?? "https://accounting.raymundgerardestaca.dev").TrimEnd('/');

        string checkoutUrl = $"{appBaseUrl}/checkout?order_id={Uri.EscapeDataString(order.Id)}";
        string? providerPaymentId = null;

        if (sessionResult.IsSuccess && !string.IsNullOrWhiteSpace(sessionResult.Value.CheckoutUrl))
        {
            providerPaymentId = sessionResult.Value.ProviderPaymentId;
            checkoutUrl = sessionResult.Value.CheckoutUrl;
            attempt.AttachCheckoutSession(
                providerPaymentId: sessionResult.Value.ProviderPaymentId,
                checkoutUrl: sessionResult.Value.CheckoutUrl,
                expiresAtUtc: sessionResult.Value.ExpiresAtUtc,
                nowUtc: nowUtc);
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        decimal amountPesos = totalAmount.AmountMinor / 100m;
        string formattedAmountPhp = $"₱{amountPesos.ToString("N2", CultureInfo.InvariantCulture)}";

        return Result.Success(new BridgedGoogleFormOrderResult(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            OrderTitle: orderTitle,
            AmountMinor: totalAmount.AmountMinor,
            AmountPhp: formattedAmountPhp,
            PaymentAttemptId: attempt.Id,
            ProviderPaymentId: providerPaymentId,
            CheckoutUrl: checkoutUrl,
            Status: order.Status));
    }

    public async Task<BridgedGoogleFormStatusResult?> ResolveLiveStatusAsync(
        string orderId,
        string? paymentAttemptId,
        CancellationToken cancellationToken = default)
    {
        Order? order = await _orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        PaymentAttempt? attempt = null;

        if (!string.IsNullOrWhiteSpace(paymentAttemptId))
        {
            attempt = await _attemptRepository.GetByIdAsync(paymentAttemptId, cancellationToken).ConfigureAwait(false);
        }

        if (attempt is null && !string.IsNullOrWhiteSpace(orderId))
        {
            attempt = await _attemptRepository.GetLatestByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        }

        if (order is null && attempt is null)
        {
            return null;
        }

        ReceiptSummaryContract? receipt = null;
        if (attempt is not null)
        {
            receipt = await _receiptLookup.FindByPaymentAttemptIdAsync(attempt.Id, cancellationToken).ConfigureAwait(false);
        }

        if (receipt is null && order is not null)
        {
            receipt = await _receiptLookup.FindByOrderIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
        }

        string effectivePaymentStatus = attempt?.Status.ToString().ToLowerInvariant()
            ?? order?.Status
            ?? "pending";

        if (string.Equals(order?.Status, OrderStatus.Paid, StringComparison.OrdinalIgnoreCase) ||
            attempt?.Status == PaymentAttemptStatus.Succeeded)
        {
            effectivePaymentStatus = "paid";
        }

        return new BridgedGoogleFormStatusResult(
            OrderId: order?.Id ?? attempt!.OrderId,
            OrderNumber: order?.OrderNumber ?? receipt?.OrderNumber ?? string.Empty,
            OrderStatus: order?.Status ?? effectivePaymentStatus,
            PaymentAttemptId: attempt?.Id,
            ProviderPaymentId: attempt?.ProviderPaymentId,
            PaymentStatus: effectivePaymentStatus,
            CheckoutUrl: attempt?.CheckoutUrl,
            AmountMinor: order?.Amount.AmountMinor ?? attempt?.Amount.AmountMinor ?? 0,
            OfficialReceiptNumber: receipt?.OfficialReceiptNumber,
            TreasuryAcknowledgementNumber: receipt?.TreasuryAcknowledgementNumber,
            PaidAtUtc: attempt?.PaidAt ?? receipt?.IssuedAtUtc);
    }
}