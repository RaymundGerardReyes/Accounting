#pragma warning disable CA1873
// File: src/Modules/Payments/Rge.Payments.Infrastructure/PayMongo/PayMongoClient.cs
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.PayMongo;

public sealed class PayMongoClient : IPaymentGateway
{
    private static readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> OfflineSessions =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> ObservedAwaitingNextActionSessions =
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

        string rawSuccess = !string.IsNullOrWhiteSpace(request.SuccessUrl)
            ? request.SuccessUrl
            : _options.SuccessUrl;
        string rawCancel = !string.IsNullOrWhiteSpace(request.CancelUrl)
            ? request.CancelUrl
            : _options.CancelUrl;

        string finalSuccessUrl = EnsureOrderIdQueryParam(rawSuccess, request.OrderId);
        string finalCancelUrl = EnsureOrderIdQueryParam(rawCancel, request.OrderId);

        var payload = new PayMongoCreateCheckoutRequest(
            new PayMongoCreateCheckoutData(
                new PayMongoCreateCheckoutAttributes(
                    Description: request.Description,
                    ReferenceNumber: request.OrderNumber,
                    SendEmailReceipt: false,
                    ShowDescription: true,
                    ShowLineItems: true,
                    SuccessUrl: finalSuccessUrl,
                    CancelUrl: finalCancelUrl,
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
            string rawCreateJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[PAYMONGO-CREATE-FAIL] HTTP {StatusCode} for Order {OrderNumber}: {ErrorBody}",
                    (int)response.StatusCode,
                    request.OrderNumber,
                    rawCreateJson);

                return Result.Failure<CheckoutSessionResult>(
                    Error.Validation("PayMongo.CheckoutCreationFailed", $"Gateway returned HTTP {(int)response.StatusCode}."));
            }

            var envelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(rawCreateJson);
            if (envelope?.Data is null || string.IsNullOrWhiteSpace(envelope.Data.Attributes.CheckoutUrl))
            {
                return Result.Failure<CheckoutSessionResult>(
                    new Error("PayMongo.MalformedResponse", "PayMongo response did not contain a valid checkout_url."));
            }

            DateTimeOffset expiresAt = envelope.Data.Attributes.ExpiredAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(envelope.Data.Attributes.ExpiredAtUnix.Value)
                : DateTimeOffset.UtcNow.AddMinutes(30);

            _logger.LogInformation(
                "[PAYMONGO-CREATE-OK] Order={OrderNumber} | cs_id={CsId} | status={Status} | expires_at={ExpiresAt:O} | url={Url}",
                request.OrderNumber,
                envelope.Data.Id,
                envelope.Data.Attributes.Status,
                expiresAt,
                envelope.Data.Attributes.CheckoutUrl);

            return Result.Success(new CheckoutSessionResult(
                ProviderPaymentId: envelope.Data.Id,
                CheckoutUrl: envelope.Data.Attributes.CheckoutUrl,
                Status: envelope.Data.Attributes.Status ?? "active",
                ExpiresAtUtc: expiresAt));
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[PAYMONGO-CREATE-TIMEOUT] Order {OrderNumber}", request.OrderNumber);
            return Result.Failure<CheckoutSessionResult>(
                new Error("PayMongo.Timeout", "Gateway request timed out."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[PAYMONGO-CREATE-NETERR] Order {OrderNumber}", request.OrderNumber);
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

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[PAYMONGO-POLL-HTTP-ERR] cs_id={CsId} | HTTP={StatusCode} | Body={Body}",
                    providerPaymentId,
                    (int)response.StatusCode,
                    rawJson);

                return Result.Failure<GatewayPaymentStatusResult>(
                    Error.NotFound("PayMongo.SessionNotFound", $"Checkout session '{providerPaymentId}' could not be retrieved."));
            }

            var envelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(rawJson);
            if (envelope?.Data is null)
            {
                _logger.LogWarning("[PAYMONGO-POLL-EMPTY] cs_id={CsId} | Raw={Raw}", providerPaymentId, rawJson);
                return Result.Failure<GatewayPaymentStatusResult>(
                    new Error("PayMongo.EmptySession", "Empty session payload returned by gateway."));
            }

            GatewayPaymentStatusResult evaluated = EvaluateCheckoutSessionStatus(envelope.Data);

            var attrs = envelope.Data.Attributes;
            string piId = attrs.PaymentIntent?.Id ?? "none";
            string piStatus = attrs.PaymentIntent?.Attributes?.Status ?? "none";
            string lastErrCode = attrs.PaymentIntent?.Attributes?.LastPaymentError?.Code
                ?? attrs.PaymentIntent?.Attributes?.LastPaymentError?.FailedCode
                ?? "none";
            int paymentsCount = attrs.Payments?.Count ?? 0;
            string latestPaymentStatus = attrs.Payments?.LastOrDefault()?.Attributes?.Status ?? "none";

            // CRITICAL FIX: When pi_status is "awaiting_next_action", PayMongo attaches a Source (src_...)
            // whose expiration/failure is ONLY visible by inspecting the underlying Source resource!
            if (string.Equals(evaluated.Status, "awaiting_next_action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(piStatus, "awaiting_next_action", StringComparison.OrdinalIgnoreCase))
            {
                GatewayPaymentStatusResult? sourceOverride = await TryInspectUnderlyingSourceAsync(
                    envelope.Data,
                    rawJson,
                    cancellationToken).ConfigureAwait(false);

                if (sourceOverride is not null)
                {
                    evaluated = sourceOverride;
                }
            }

            _logger.LogInformation(
                "[PAYMONGO-POLL-DIAG] cs_id={CsId} | cs_status={CsStatus} | pi_id={PiId} | pi_status={PiStatus} | payments_count={PayCount} | latest_pay_status={LatestPayStatus} | last_err={LastErr} => EVALUATED={EvalStatus} (code={FailCode})",
                envelope.Data.Id,
                attrs.Status ?? "null",
                piId,
                piStatus,
                paymentsCount,
                latestPaymentStatus,
                lastErrCode,
                evaluated.Status,
                evaluated.FailureCode ?? "none");

            return Result.Success(evaluated);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "[PAYMONGO-POLL-EX] Failed to poll PayMongo session {ProviderPaymentId}", providerPaymentId);
            return Result.Failure<GatewayPaymentStatusResult>(
                new Error("PayMongo.PollFailed", "Gateway status poll failed due to network or timeout error."));
        }
    }

    /// <summary>
    /// Inspects the underlying Source resource (src_...) and PaymentIntent (pi_...)
    /// to detect if an e-wallet authorization has expired, failed, or cancelled while
    /// the parent checkout session remains in 'awaiting_next_action'.
    /// </summary>
    private async Task<GatewayPaymentStatusResult?> TryInspectUnderlyingSourceAsync(
        PayMongoCheckoutSessionData sessionData,
        string checkoutSessionRawJson,
        CancellationToken cancellationToken)
    {
        if (IsOfflineMode)
        {
            return null;
        }

        var attrs = sessionData.Attributes;
        string? redirectUrl = attrs.PaymentIntent?.Attributes?.NextAction?.Redirect?.Url;
        string? pmId = attrs.PaymentIntent?.Attributes?.PaymentMethodId;
        string? piId = attrs.PaymentIntent?.Id ?? ExtractIdWithPrefix(checkoutSessionRawJson, "pi_");

        // 1. Cascading extraction of src_ from redirect.url, payment_method_id, or raw checkout session JSON
        string? srcId = ExtractIdWithPrefix(redirectUrl, "src_")
            ?? ExtractIdWithPrefix(pmId, "src_")
            ?? ExtractIdWithPrefix(checkoutSessionRawJson, "src_");

        // 2. Fallback: If src_ was not embedded in the checkout session payload, query GET /v1/payment_intents/{pi_id}
        if (string.IsNullOrWhiteSpace(srcId) && !string.IsNullOrWhiteSpace(piId) && piId.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using HttpResponseMessage piResp = await _httpClient.GetAsync(
                    $"payment_intents/{Uri.EscapeDataString(piId)}",
                    cancellationToken).ConfigureAwait(false);

                if (piResp.IsSuccessStatusCode)
                {
                    string piRawJson = await piResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    srcId = ExtractIdWithPrefix(piRawJson, "src_");

                    var piEnvelope = JsonSerializer.Deserialize<PayMongoPaymentIntentEnvelope>(piRawJson);
                    var piAttrs = piEnvelope?.Data?.Attributes;
                    var piLastErr = piAttrs?.LastPaymentError;

                    if (piLastErr is not null)
                    {
                        string errCode = piLastErr.FailedCode ?? piLastErr.Code ?? "payment_intent_error";
                        string errMsg = piLastErr.FailedMessage ?? piLastErr.Detail ?? "Payment failed inside PayMongo.";

                        _logger.LogWarning(
                            "[PAYMONGO-PI-ERROR-DIAG] cs_id={CsId} | pi_id={PiId} | err_code={ErrCode} | msg={ErrMsg}",
                            sessionData.Id,
                            piId,
                            errCode,
                            errMsg);

                        ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                        return new GatewayPaymentStatusResult(
                            ProviderPaymentId: sessionData.Id,
                            Status: "failed",
                            PaymentMethodUsed: attrs.PaymentMethodUsed,
                            PaidAtUtc: null,
                            FailureCode: errCode,
                            FailureMessage: errMsg);
                    }

                    string piEndpointStatus = (piAttrs?.Status ?? string.Empty).Trim().ToLowerInvariant();
                    if (piEndpointStatus is "cancelled")
                    {
                        ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                        return new GatewayPaymentStatusResult(
                            ProviderPaymentId: sessionData.Id,
                            Status: "expired",
                            PaymentMethodUsed: attrs.PaymentMethodUsed,
                            PaidAtUtc: null,
                            FailureCode: "payment_intent_cancelled",
                            FailureMessage: "Payment intent was cancelled on PayMongo.");
                    }

                    if (piEndpointStatus is "failed")
                    {
                        ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                        return new GatewayPaymentStatusResult(
                            ProviderPaymentId: sessionData.Id,
                            Status: "failed",
                            PaymentMethodUsed: attrs.PaymentMethodUsed,
                            PaidAtUtc: null,
                            FailureCode: "payment_intent_failed",
                            FailureMessage: "Payment intent failed on PayMongo.");
                    }

                    if (piEndpointStatus is "succeeded")
                    {
                        ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                        return new GatewayPaymentStatusResult(
                            ProviderPaymentId: sessionData.Id,
                            Status: "paid",
                            PaymentMethodUsed: attrs.PaymentMethodUsed,
                            PaidAtUtc: DateTimeOffset.UtcNow,
                            FailureCode: null,
                            FailureMessage: null);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "[PAYMONGO-PI-HTTP-ERR] cs_id={CsId} | pi_id={PiId} | HTTP={StatusCode}",
                        sessionData.Id,
                        piId,
                        (int)piResp.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _logger.LogWarning(ex, "[PAYMONGO-PI-POLL-WARN] Could not inspect PaymentIntent {PiId}", piId);
            }
        }

        if (string.IsNullOrWhiteSpace(srcId))
        {
            _logger.LogInformation(
                "[PAYMONGO-SOURCE-DIAG] cs_id={CsId} | pi_id={PiId} | next_action_url={NextUrl} | No src_ token found yet.",
                sessionData.Id,
                piId ?? "none",
                redirectUrl ?? "none");
            return null;
        }

        // 3. Query GET /v1/sources/{src_id} to inspect authentic e-wallet Source status
        try
        {
            using HttpResponseMessage srcResp = await _httpClient.GetAsync(
                $"sources/{Uri.EscapeDataString(srcId)}",
                cancellationToken).ConfigureAwait(false);

            if (!srcResp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[PAYMONGO-SOURCE-HTTP-ERR] cs_id={CsId} | src_id={SrcId} | HTTP={StatusCode}",
                    sessionData.Id,
                    srcId,
                    (int)srcResp.StatusCode);
                return null;
            }

            string srcRawJson = await srcResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var srcEnvelope = JsonSerializer.Deserialize<PayMongoSourceEnvelope>(srcRawJson);
            string srcStatus = (srcEnvelope?.Data?.Attributes?.Status ?? "unknown").Trim().ToLowerInvariant();

            _logger.LogInformation(
                "[PAYMONGO-SOURCE-DIAG] cs_id={CsId} | pi_id={PiId} | src_id={SrcId} | src_status={SrcStatus}",
                sessionData.Id,
                piId ?? "none",
                srcId,
                srcStatus);

            if (srcStatus is "expired" or "cancelled")
            {
                ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                return new GatewayPaymentStatusResult(
                    ProviderPaymentId: sessionData.Id,
                    Status: "expired",
                    PaymentMethodUsed: attrs.PaymentMethodUsed ?? srcEnvelope?.Data?.Attributes?.SourceType,
                    PaidAtUtc: null,
                    FailureCode: $"source_{srcStatus}",
                    FailureMessage: $"Source {srcId} has {srcStatus} status.");
            }

            if (srcStatus is "failed")
            {
                ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                return new GatewayPaymentStatusResult(
                    ProviderPaymentId: sessionData.Id,
                    Status: "failed",
                    PaymentMethodUsed: attrs.PaymentMethodUsed ?? srcEnvelope?.Data?.Attributes?.SourceType,
                    PaidAtUtc: null,
                    FailureCode: "source_failed",
                    FailureMessage: $"Source {srcId} failed authorization on PayMongo.");
            }

            if (srcStatus is "paid")
            {
                ObservedAwaitingNextActionSessions.TryRemove(sessionData.Id, out _);

                return new GatewayPaymentStatusResult(
                    ProviderPaymentId: sessionData.Id,
                    Status: "paid",
                    PaymentMethodUsed: attrs.PaymentMethodUsed ?? srcEnvelope?.Data?.Attributes?.SourceType,
                    PaidAtUtc: DateTimeOffset.UtcNow,
                    FailureCode: null,
                    FailureMessage: null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning(ex, "[PAYMONGO-SOURCE-POLL-WARN] Failed querying Source {SrcId}", srcId);
        }

        return null;
    }

    /// <summary>
    /// Fast, allocation-safe extraction of a PayMongo resource ID (e.g. "src_ba7hPZAKRbxYVCFXT7LFao9q")
    /// from any URL or JSON string.
    /// </summary>
    public static string? ExtractIdWithPrefix(string? input, string prefix)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        int startIdx = input.IndexOf(prefix, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            return null;
        }

        int endIdx = startIdx + prefix.Length;
        while (endIdx < input.Length && char.IsLetterOrDigit(input[endIdx]))
        {
            endIdx++;
        }

        int tokenLength = endIdx - startIdx;
        return tokenLength > prefix.Length + 8
            ? input.Substring(startIdx, tokenLength)
            : null;
    }

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

        if (intentStatus is "awaiting_next_action" or "processing")
        {
            ObservedAwaitingNextActionSessions[data.Id] = true;
        }

        // Priority 1: Succeeded / Paid
        if (paymentStatus is "paid" or "succeeded" ||
            intentStatus is "succeeded" ||
            sessionStatus is "paid" ||
            paidAt.HasValue)
        {
            ObservedAwaitingNextActionSessions.TryRemove(data.Id, out _);
            return new GatewayPaymentStatusResult(
                ProviderPaymentId: data.Id,
                Status: "paid",
                PaymentMethodUsed: attrs.PaymentMethodUsed,
                PaidAtUtc: paidAt ?? DateTimeOffset.UtcNow,
                FailureCode: null,
                FailureMessage: null);
        }

        // Priority 2: Failed payment OR Source dropped from awaiting_next_action back to awaiting_payment_method
        bool sourceExpiredOrFailedAfterAction =
            ObservedAwaitingNextActionSessions.ContainsKey(data.Id) &&
            intentStatus is "awaiting_payment_method";

        if (paymentStatus is "failed" || lastError is not null || sourceExpiredOrFailedAfterAction)
        {
            ObservedAwaitingNextActionSessions.TryRemove(data.Id, out _);

            string failureCode = latestPayment?.FailedCode
                ?? lastError?.FailedCode
                ?? lastError?.Code
                ?? attrs.FailedCode
                ?? (sourceExpiredOrFailedAfterAction ? "source_expired_or_failed" : "payment_failed");

            string failureMessage = latestPayment?.FailedMessage
                ?? lastError?.FailedMessage
                ?? lastError?.Detail
                ?? attrs.FailedMessage
                ?? (sourceExpiredOrFailedAfterAction
                    ? "The underlying payment source (src_) expired or failed authorization on PayMongo."
                    : "Payment failed at gateway.");

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
            ObservedAwaitingNextActionSessions.TryRemove(data.Id, out _);
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
            : intentStatus is "processing" or "awaiting_next_action"
                ? intentStatus
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

        ObservedAwaitingNextActionSessions.TryRemove(providerPaymentId, out _);

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

            string respBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation(
                "[PAYMONGO-EXPIRE-API] cs_id={CsId} | HTTP={StatusCode} | Success={IsSuccess}",
                providerPaymentId,
                (int)response.StatusCode,
                response.IsSuccessStatusCode);

            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(Error.Validation("PayMongo.ExpireFailed", $"Could not expire session '{providerPaymentId}': {respBody}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "[PAYMONGO-EXPIRE-EX] Error expiring PayMongo session {ProviderPaymentId}", providerPaymentId);
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

    private static string EnsureOrderIdQueryParam(string url, string orderId)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (url.Contains("order_id=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        string separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}order_id={Uri.EscapeDataString(orderId)}";
    }
}
