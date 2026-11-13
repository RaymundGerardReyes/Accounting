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

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Cross-module bridge that persists Google Form submissions as real Orders in PostgreSQL/memory
/// and initiates live PayMongo Checkout Sessions.
/// </summary>
public sealed class GoogleFormOrderBridge : IGoogleFormOrderBridge
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IPricingPolicy _pricingPolicy;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public GoogleFormOrderBridge(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository,
        IPricingPolicy pricingPolicy,
        IPaymentGateway paymentGateway,
        IClock clock,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(pricingPolicy);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(configuration);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
        _pricingPolicy = pricingPolicy;
        _paymentGateway = paymentGateway;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<GoogleFormOrderCreationResult> CreateOrderAndInitiateCheckoutAsync(
        GoogleFormOrderCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset nowUtc = _clock.UtcNow;
        string normalizedCategory = string.Equals(request.Category, OrderCategory.DepartmentInvoice, StringComparison.OrdinalIgnoreCase)
            ? OrderCategory.DepartmentInvoice
            : OrderCategory.ProductOrder;

        string cleanReference = string.IsNullOrWhiteSpace(request.ItemReference)
            ? "CPA Reviewer Volume 1"
            : request.ItemReference.Trim();

        int safeQty = Math.Max(1, request.Quantity);

        Result<Money> priceResult = _pricingPolicy.CalculateAuthoritativeAmount(
            normalizedCategory,
            cleanReference,
            safeQty,
            request.AmountMinor);

        Money totalAmount = priceResult.IsSuccess
            ? priceResult.Value
            : Money.Php(request.AmountMinor is > 0 ? request.AmountMinor.Value : safeQty * 450_00L);

        string prefix = normalizedCategory == OrderCategory.ProductOrder ? "ORD" : "INV";
        string yearMonth = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        long seq = await _orderRepository.NextOrderSequenceAsync(prefix, yearMonth, cancellationToken).ConfigureAwait(false);
        OrderNumber orderNumber = OrderNumber.Create(normalizedCategory, nowUtc, seq);

        string orderTitle = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title.Trim()
            : (safeQty > 1 ? $"{cleanReference} (x{safeQty})" : cleanReference);

        var metadataObj = new
        {
            source = "google_forms",
            google_form_response_id = request.ResponseId,
            sheet_row_index = request.SheetRowIndex ?? 0,
            customer_name = request.CustomerName,
            customer_email = request.CustomerEmail,
            customer_phone = request.CustomerPhone ?? string.Empty,
            student_id = request.StudentId ?? string.Empty,
            instructor_credit = request.InstructorCredit ?? string.Empty,
            book_title = request.BookTitle ?? cleanReference,
            quantity = safeQty,
            notes = request.Notes ?? string.Empty
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

        string normalizedMethod = PaymentMethod.Normalize(request.PaymentMethod);
        string idempotencyKey = $"gf_attempt_{request.ResponseId}";

        PaymentAttempt attempt = PaymentAttempt.CreatePending(
            orderId: order.Id,
            paymentMethod: normalizedMethod,
            amount: totalAmount,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        var gatewayRequest = new CreateCheckoutSessionRequest(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Description: $"{order.OrderNumber} - {orderTitle}",
            Amount: totalAmount,
            RequestedPaymentMethod: normalizedMethod,
            IdempotencyKey: idempotencyKey,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            CustomerPhone: request.CustomerPhone,
            LineItems: [],
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

        return new GoogleFormOrderCreationResult(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            OrderTitle: order.Title,
            AmountMinor: totalAmount.AmountMinor,
            Currency: totalAmount.CurrencyCode,
            CheckoutUrl: checkoutUrl,
            PaymentAttemptId: attempt.Id,
            ProviderPaymentId: providerPaymentId);
    }

    public async Task<GoogleFormOrderCreationResult?> FindExistingByResponseIdAsync(
        string responseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(responseId))
        {
            return null;
        }

        string idempotencyKey = $"gf_attempt_{responseId.Trim()}";
        PaymentAttempt? existingAttempt = await _attemptRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existingAttempt is not null)
        {
            Order? matchedOrder = await _orderRepository
                .GetByIdAsync(existingAttempt.OrderId, cancellationToken)
                .ConfigureAwait(false);

            if (matchedOrder is not null)
            {
                return new GoogleFormOrderCreationResult(
                    OrderId: matchedOrder.Id,
                    OrderNumber: matchedOrder.OrderNumber,
                    OrderTitle: matchedOrder.Title,
                    AmountMinor: matchedOrder.Amount.AmountMinor,
                    Currency: matchedOrder.Amount.CurrencyCode,
                    CheckoutUrl: existingAttempt.CheckoutUrl ?? $"/checkout?order_id={matchedOrder.Id}",
                    PaymentAttemptId: existingAttempt.Id,
                    ProviderPaymentId: existingAttempt.ProviderPaymentId);
            }
        }

        IReadOnlyList<Order> recentOrders = await _orderRepository
            .ListAsync(status: null, limit: 100, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (Order order in recentOrders)
        {
            if (!string.IsNullOrWhiteSpace(order.MetadataJson) &&
                order.MetadataJson.Contains(responseId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                PaymentAttempt? latestAttempt = await _attemptRepository
                    .GetLatestByOrderIdAsync(order.Id, cancellationToken)
                    .ConfigureAwait(false);

                return new GoogleFormOrderCreationResult(
                    OrderId: order.Id,
                    OrderNumber: order.OrderNumber,
                    OrderTitle: order.Title,
                    AmountMinor: order.Amount.AmountMinor,
                    Currency: order.Amount.CurrencyCode,
                    CheckoutUrl: latestAttempt?.CheckoutUrl ?? $"/checkout?order_id={order.Id}",
                    PaymentAttemptId: latestAttempt?.Id ?? $"att_{order.Id[..Math.Min(8, order.Id.Length)]}",
                    ProviderPaymentId: latestAttempt?.ProviderPaymentId);
            }
        }

        return null;
    }

    public async Task<(int Total, int Paid, int Pending)> GetGoogleFormOrderStatsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> orders = await _orderRepository
            .ListAsync(status: null, limit: 100, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        int total = 0;
        int paid = 0;
        int pending = 0;

        foreach (Order order in orders)
        {
            bool isGoogleFormOrder =
                string.Equals(order.UserId, "google_forms_bridge", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(order.MetadataJson) &&
                 order.MetadataJson.Contains("google_forms", StringComparison.OrdinalIgnoreCase));

            if (!isGoogleFormOrder)
            {
                continue;
            }

            total++;
            if (string.Equals(order.Status, OrderStatus.Paid, StringComparison.OrdinalIgnoreCase))
            {
                paid++;
            }
            else if (string.Equals(order.Status, OrderStatus.Pending, StringComparison.OrdinalIgnoreCase))
            {
                pending++;
            }
        }

        return (total, paid, pending);
    }
}