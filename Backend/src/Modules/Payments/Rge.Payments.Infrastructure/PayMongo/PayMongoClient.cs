using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Typed HTTP client for PayMongo API v1 (Checkout Sessions, Status Polling, Session Expiry).
/// Uses HTTP Basic Auth: Base64("{SecretKey}:").
/// </summary>
public sealed class PayMongoClient : IPaymentGateway
{
    private static readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> OfflineSessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly PayMongoOptions _options;
    private readonly ILogger<PayMongoClient> _logger;

    public PayMongoClient(
        HttpClient httpClient,
        IOptions<PayMongoOptions> options,
        ILogger<PayMongoClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        ConfigureHttpClient();
    }

    private bool IsOfflineMode =>
        string.IsNullOrWhiteSpace(_options.SecretKey) ||
        _options.SecretKey.StartsWith("sk_test_offline", StringComparison.OrdinalIgnoreCase);

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 2, 30));
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            byte[] authBytes = Encoding.UTF8.GetBytes($"{_options.SecretKey}:");
            string base64Auth = Convert.ToBase64String(authBytes);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
        }
    }

    public async Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsOfflineMode)
        {
            string syntheticId = $"cs_offline_{Guid.NewGuid():N}";
            DateTimeOffset offlineExpiry = DateTimeOffset.UtcNow.AddMinutes(30);

            OfflineSessions[syntheticId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: syntheticId,
                Status: "active",
                PaymentMethodUsed: request.RequestedPaymentMethod,
                PaidAtUtc: null,
                FailureCode: null,
                FailureMessage: null);

            return Result.Success(new CheckoutSessionResult(
                ProviderPaymentId: syntheticId,
                CheckoutUrl: $"https://checkout.paymongo.com/{syntheticId}",
                Status: "active",
                ExpiresAtUtc: offlineExpiry));
        }

        IReadOnlyList<string> paymentMethodTypes = MapPaymentMethodTypes(request.RequestedPaymentMethod);

        IReadOnlyList<PayMongoLineItem> lineItems = request.LineItems.Count > 0
            ? request.LineItems
                .Select(li => new PayMongoLineItem(li.Name, li.Quantity, li.AmountMinor, li.Currency, li.Description))
                .ToList()
            : [new PayMongoLineItem(request.Description, 1, request.Amount.AmountMinor, request.Amount.CurrencyCode)];

        var payload = new PayMongoCreateCheckoutRequest(
            new PayMongoCreateCheckoutData(
                new PayMongoCreateCheckoutAttributes(
                    Description: request.Description,
                    ReferenceNumber: request.OrderNumber,
                    SendEmailReceipt: false, // R.G.E issues its own BIR RMC 77-2024 Official Receipt via outbox
                    ShowDescription: true,
                    ShowLineItems: true,
                    SuccessUrl: $"{_options.SuccessUrl}?order_id={Uri.EscapeDataString(request.OrderId)}",
                    CancelUrl: $"{_options.CancelUrl}?order_id={Uri.EscapeDataString(request.OrderId)}",
                    PaymentMethodTypes: paymentMethodTypes,
                    LineItems: lineItems,
                    Billing: new PayMongoBillingInfo(request.CustomerName, request.CustomerEmail, request.CustomerPhone),
                    Metadata: new Dictionary<string, string>
                    {
                        ["order_id"] = request.OrderId,
                        ["order_number"] = request.OrderNumber,
                        ["idempotency_key"] = request.IdempotencyKey
                    })));

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "checkout_sessions")
            {
                Content = JsonContent.Create(payload)
            };
            httpRequest.Headers.Add("Idempotency-Key", request.IdempotencyKey);

            using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "PayMongo checkout_sessions creation failed with HTTP {StatusCode} for Order {OrderNumber}: {ErrorBody}",
                    (int)response.StatusCode,
                    request.OrderNumber,
                    errorBody);

                return Result.Failure<CheckoutSessionResult>(
                    Error.Validation("PayMongo.CheckoutCreationFailed", $"Gateway returned HTTP {(int)response.StatusCode}."));
            }

            var envelope = await response.Content.ReadFromJsonAsync<PayMongoCheckoutSessionEnvelope>(cancellationToken);
            if (envelope?.Data is null || string.IsNullOrWhiteSpace(envelope.Data.Attributes.CheckoutUrl))
            {
                return Result.Failure<CheckoutSessionResult>(
                    new Error("PayMongo.MalformedResponse", "PayMongo response did not contain a valid checkout_url."));
            }

            DateTimeOffset expiresAt = envelope.Data.Attributes.ExpiredAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(envelope.Data.Attributes.ExpiredAtUnix.Value)
                : DateTimeOffset.UtcNow.AddMinutes(30);

            return Result.Success(new CheckoutSessionResult(
                ProviderPaymentId: envelope.Data.Id,
                CheckoutUrl: envelope.Data.Attributes.CheckoutUrl,
                Status: envelope.Data.Attributes.Status ?? "active",
                ExpiresAtUtc: expiresAt));
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "PayMongo checkout creation timed out for Order {OrderNumber}", request.OrderNumber);
            return Result.Failure<CheckoutSessionResult>(
                new Error("PayMongo.Timeout", "Gateway request timed out."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayMongo network error for Order {OrderNumber}", request.OrderNumber);
            return Result.Failure<CheckoutSessionResult>(
                new Error("PayMongo.NetworkError", "Unable to reach PayMongo gateway."));
        }
    }

    public async Task<Result<GatewayPaymentStatusResult>> GetCheckoutSessionStatusAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Result.Failure<GatewayPaymentStatusResult>(
                Error.Validation("PayMongo.MissingSessionId", "Provider payment ID is required."));
        }

        if (IsOfflineMode || providerPaymentId.StartsWith("cs_offline_", StringComparison.OrdinalIgnoreCase))
        {
            if (OfflineSessions.TryGetValue(providerPaymentId, out GatewayPaymentStatusResult? offlineStatus))
            {
                return Result.Success(offlineStatus);
            }

            return Result.Success(new GatewayPaymentStatusResult(
                ProviderPaymentId: providerPaymentId,
                Status: "active",
                PaymentMethodUsed: null,
                PaidAtUtc: null,
                FailureCode: null,
                FailureMessage: null));
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"checkout_sessions/{Uri.EscapeDataString(providerPaymentId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<GatewayPaymentStatusResult>(
                    Error.NotFound("PayMongo.SessionNotFound", $"Checkout session '{providerPaymentId}' could not be retrieved."));
            }

            var envelope = await response.Content.ReadFromJsonAsync<PayMongoCheckoutSessionEnvelope>(cancellationToken);
            if (envelope?.Data is null)
            {
                return Result.Failure<GatewayPaymentStatusResult>(
                    new Error("PayMongo.EmptySession", "Empty session payload returned by gateway."));
            }

            return Result.Success(EvaluateCheckoutSessionStatus(envelope.Data));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to poll PayMongo session {ProviderPaymentId}", providerPaymentId);
            return Result.Failure<GatewayPaymentStatusResult>(
                new Error("PayMongo.PollFailed", "Gateway status poll failed due to network or timeout error."));
        }
    }

    /// <summary>
    /// Evaluates the true status of a PayMongo Checkout Session by inspecting:
    /// 1) Nested payments[] array
    /// 2) Nested payment_intent (status + last_payment_error)
    /// 3) Top-level Checkout Session status and expired_at timestamp
    /// </summary>
    public static GatewayPaymentStatusResult EvaluateCheckoutSessionStatus(PayMongoCheckoutSessionData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var attrs = data.Attributes;
        var latestPayment = attrs.Payments?.LastOrDefault()?.Attributes;
        var paymentIntent = attrs.PaymentIntent?.Attributes;
        var lastError = paymentIntent?.LastPaymentError;

        DateTimeOffset? paidAt = attrs.PaidAtUnix.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(attrs.PaidAtUnix.Value)
            : latestPayment?.PaidAtUnix.HasValue == true
                ? DateTimeOffset.FromUnixTimeSeconds(latestPayment.PaidAtUnix.Value)
                : null;

        string sessionStatus = (attrs.Status ?? string.Empty).Trim().ToLowerInvariant();
        string paymentStatus = (latestPayment?.Status ?? string.Empty).Trim().ToLowerInvariant();
        string intentStatus = (paymentIntent?.Status ?? string.Empty).Trim().ToLowerInvariant();

        // Priority 1: Succeeded / Paid
        if (paymentStatus is "paid" or "succeeded" ||
            intentStatus is "succeeded" ||
            sessionStatus is "paid" ||
            paidAt.HasValue)
        {
            return new GatewayPaymentStatusResult(
                ProviderPaymentId: data.Id,
                Status: "paid",
                PaymentMethodUsed: attrs.PaymentMethodUsed,
                PaidAtUtc: paidAt ?? DateTimeOffset.UtcNow,
                FailureCode: null,
                FailureMessage: null);
        }

        // Priority 2: Failed payment (either in payments[] or in payment_intent.last_payment_error)
        if (paymentStatus is "failed" || lastError is not null)
        {
            string failureCode = latestPayment?.FailedCode
                ?? lastError?.FailedCode
                ?? lastError?.Code
                ?? attrs.FailedCode
                ?? "payment_failed";

            string failureMessage = latestPayment?.FailedMessage
                ?? lastError?.FailedMessage
                ?? lastError?.Detail
                ?? attrs.FailedMessage
                ?? "Payment failed at gateway.";

            return new GatewayPaymentStatusResult(
                ProviderPaymentId: data.Id,
                Status: "failed",
                PaymentMethodUsed: attrs.PaymentMethodUsed,
                PaidAtUtc: null,
                FailureCode: failureCode,
                FailureMessage: failureMessage);
        }

        // Priority 3: Expired Checkout Session or Cancelled Payment Intent
        bool isPastExpiredAtUnix = attrs.ExpiredAtUnix.HasValue &&
            DateTimeOffset.FromUnixTimeSeconds(attrs.ExpiredAtUnix.Value) <= DateTimeOffset.UtcNow;

        if (sessionStatus is "expired" || intentStatus is "cancelled" || isPastExpiredAtUnix)
        {
            return new GatewayPaymentStatusResult(
                ProviderPaymentId: data.Id,
                Status: "expired",
                PaymentMethodUsed: attrs.PaymentMethodUsed,
                PaidAtUtc: null,
                FailureCode: "session_expired",
                FailureMessage: "PayMongo checkout session has expired.");
        }

        // Priority 4: Processing / Awaiting Next Action / Active
        string activeOrProcessing = !string.IsNullOrWhiteSpace(paymentStatus)
            ? paymentStatus
            : !string.IsNullOrWhiteSpace(intentStatus) && intentStatus is "processing"
                ? "processing"
                : !string.IsNullOrWhiteSpace(sessionStatus)
                    ? sessionStatus
                    : "active";

        return new GatewayPaymentStatusResult(
            ProviderPaymentId: data.Id,
            Status: activeOrProcessing,
            PaymentMethodUsed: attrs.PaymentMethodUsed,
            PaidAtUtc: null,
            FailureCode: null,
            FailureMessage: null);
    }

    public async Task<Result> ExpireCheckoutSessionAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Result.Failure(Error.Validation("PayMongo.MissingSessionId", "Provider payment ID is required."));
        }

        if (IsOfflineMode || providerPaymentId.StartsWith("cs_offline_", StringComparison.OrdinalIgnoreCase))
        {
            OfflineSessions[providerPaymentId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: providerPaymentId,
                Status: "expired",
                PaymentMethodUsed: null,
                PaidAtUtc: null,
                FailureCode: "session_expired",
                FailureMessage: "Checkout session expired.");
            return Result.Success();
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsync(
                $"checkout_sessions/{Uri.EscapeDataString(providerPaymentId)}/expire",
                content: null,
                cancellationToken);

            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(Error.Validation("PayMongo.ExpireFailed", $"Could not expire session '{providerPaymentId}'."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Error expiring PayMongo session {ProviderPaymentId}", providerPaymentId);
            return Result.Failure(new Error("PayMongo.ExpireNetworkError", "Gateway network error while expiring session."));
        }
    }

    public async Task<Result<bool>> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return Result.Failure<bool>(Error.Validation("PayMongo.MissingSecretKey", "PayMongo secret key is not configured."));
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync("webhooks", cancellationToken);
            return response.IsSuccessStatusCode
                ? Result.Success(true)
                : Result.Failure<bool>(Error.Unauthorized("PayMongo.InvalidCredentials", $"PayMongo rejected API key with HTTP {(int)response.StatusCode}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayMongo diagnostic probe failed");
            return Result.Failure<bool>(new Error("PayMongo.ProbeUnreachable", "Could not connect to PayMongo API."));
        }
    }

    private static IReadOnlyList<string> MapPaymentMethodTypes(string requestedMethod) =>
        requestedMethod.Trim().ToUpperInvariant() switch
        {
            "QRPH" => ["qrph"],
            "GCASH" => ["gcash"],
            "PAYMAYA" or "MAYA" => ["paymaya"],
            "CARD" or "CARD_VISA" or "CARD_MASTERCARD" or "CARD_JCB" or "CARD_AMEX" => ["card"],
            "DOB" => ["dob", "dob_ubp"],
            "DOB_BPI" => ["dob"],
            "DOB_UBP" => ["dob_ubp"],
            "HOSTED_CHECKOUT" or _ => ["qrph", "card", "gcash", "paymaya", "dob", "dob_ubp"]
        };
}
