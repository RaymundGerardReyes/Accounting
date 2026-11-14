using System.Globalization;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.Services;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;
using SmtpClientFactory = Rge.Integrations.Application.Ports.ISmtpClientFactory;

namespace Rge.Integrations.Application.SubmitGoogleForm;

public sealed record SubmitGoogleFormCommand(
    GoogleFormSubmissionRequest Request,
    string? ProvidedSecret) : ICommand<GoogleFormSubmissionResponse>;

public sealed record SubmitGoogleFormInput(
    string? ResponseId,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? ProductCode,
    string? ProductTitle,
    int Quantity,
    long? AmountMinor,
    decimal? AmountMajor,
    string? Currency,
    string? Status,
    string? Timestamp = null,
    bool SendInitialEmail = true,
    Dictionary<string, string>? Metadata = null,
    string? Department = null,
    string? CostCenter = null);

public sealed record SubmitGoogleFormResponse(
    bool Ok,
    string OrderId,
    string OrderNumber,
    string ResponseId,
    string Status,
    bool CreatedNew,
    bool EmailDispatched,
    string? EmailMessage,
    DateTimeOffset CreatedAt);

public sealed class SubmitGoogleFormHandler : ICommandHandler<SubmitGoogleFormCommand, GoogleFormSubmissionResponse>
{
    private readonly IGoogleFormsSubmissionStore _store;
    private readonly ISecretStore _secretStore;
    private readonly IGoogleSheetsClient _sheetsClient;
    private readonly IClock _clock;
    private readonly FormSecretVerifier _secretVerifier;
    private readonly IGoogleFormOrderBridge? _orderBridge;
    private readonly ISmtpClientFactory? _smtpClientFactory;
    private readonly Microsoft.Extensions.Logging.ILogger<SubmitGoogleFormHandler>? _logger;

    public SubmitGoogleFormHandler(
        IGoogleFormsSubmissionStore store,
        ISecretStore secretStore,
        IGoogleSheetsClient sheetsClient,
        IClock clock,
        FormSecretVerifier? secretVerifier = null,
        IGoogleFormOrderBridge? orderBridge = null,
        ISmtpClientFactory? smtpClientFactory = null,
        Microsoft.Extensions.Logging.ILogger<SubmitGoogleFormHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(sheetsClient);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _secretStore = secretStore;
        _sheetsClient = sheetsClient;
        _clock = clock;
        _secretVerifier = secretVerifier ?? new FormSecretVerifier(secretStore);
        _orderBridge = orderBridge;
        _smtpClientFactory = smtpClientFactory;
        _logger = logger;
    }

    public bool VerifySecret(string? providedSecret) => _secretVerifier.IsValid(providedSecret);

    public async Task<SubmitGoogleFormResponse> HandleAsync(
        SubmitGoogleFormInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        DateTimeOffset submittedAt = ParseHistoricalTimestamp(input.Timestamp);

        string responseId = !string.IsNullOrWhiteSpace(input.ResponseId)
            ? input.ResponseId.Trim()
            : $"GFR-{submittedAt:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        string customerName = !string.IsNullOrWhiteSpace(input.CustomerName)
            ? input.CustomerName.Trim()
            : "Google Forms Customer";

        string customerEmail = (input.CustomerEmail ?? string.Empty).Trim();
        string? customerPhone = string.IsNullOrWhiteSpace(input.CustomerPhone) ? null : input.CustomerPhone.Trim();

        string productCode = !string.IsNullOrWhiteSpace(input.ProductCode)
            ? input.ProductCode.Trim()
            : "COST-ACC-TEXT-2026";

        string productTitle = !string.IsNullOrWhiteSpace(input.ProductTitle)
            ? input.ProductTitle.Trim()
            : "Cost Accounting Textbook 2026";

        int quantity = input.Quantity > 0 ? input.Quantity : 1;

        long totalAmountMinor = ResolveAmountMinor(input.AmountMinor, input.AmountMajor, productCode, quantity);
        long unitPriceMinor = quantity > 0 ? totalAmountMinor / quantity : totalAmountMinor;

        string currency = !string.IsNullOrWhiteSpace(input.Currency)
            ? input.Currency.Trim().ToUpperInvariant()
            : "PHP";

        string status = NormalizeOrderStatus(input.Status);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"] = customerName,
            ["customer_email"] = customerEmail,
            ["source"] = "google_forms",
            ["google_form_response_id"] = responseId
        };

        if (!string.IsNullOrWhiteSpace(customerPhone))
        {
            metadata["customer_phone"] = customerPhone;
        }

        if (input.Metadata is not null)
        {
            foreach (var kvp in input.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        string orderId = $"ord_{responseId}";
        string orderNumber = $"ORD-{submittedAt:yyyyMM}-{responseId[..Math.Min(6, responseId.Length)].ToUpperInvariant()}";
        bool createdNew = true;
        bool emailDispatched = false;
        string? emailMessage = null;
        string checkoutUrl = $"/checkout?order_id={orderId}";
        string paymentAttemptId = $"att_{orderId}";
        string? providerPaymentId = null;

        if (_orderBridge is not null)
        {
            var bridgeRequest = new GoogleFormOrderBridgeRequest(
                ResponseId: responseId,
                CustomerName: customerName,
                CustomerEmail: customerEmail,
                CustomerPhone: customerPhone,
                ProductCode: productCode,
                ProductTitle: productTitle,
                Quantity: quantity,
                UnitPriceMinor: unitPriceMinor,
                TotalAmountMinor: totalAmountMinor,
                Currency: currency,
                Status: status,
                SubmittedAt: submittedAt,
                SendInitialEmail: input.SendInitialEmail,
                AdditionalMetadata: metadata);

            BridgedGoogleFormOrder bridgeResult = await _orderBridge.BridgeSubmissionToOrderAsync(bridgeRequest, ct).ConfigureAwait(false);
            orderId = bridgeResult.OrderId;
            orderNumber = bridgeResult.OrderNumber;
            status = bridgeResult.Status;
            createdNew = bridgeResult.CreatedNew;
            emailDispatched = bridgeResult.EmailDispatched;
            emailMessage = bridgeResult.EmailStatusMessage;
            checkoutUrl = bridgeResult.CheckoutUrl;
            paymentAttemptId = bridgeResult.PaymentAttemptId;
            providerPaymentId = bridgeResult.ProviderPaymentId;
        }

        if (_smtpClientFactory is not null && input.SendInitialEmail && !string.IsNullOrWhiteSpace(customerEmail) && customerEmail.Contains('@', StringComparison.Ordinal))
        {
            _ = Task.Run(async () =>
            {
                using var detachedCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                            "[GOOGLE-FORM-SMTP] Dispatching payment invitation email for Order {OrderNumber} ({OrderId}) to {Recipient}",
                            orderNumber, orderId, customerEmail);
                    }

                    var sendResult = await _smtpClientFactory.SendPaymentInvitationEmailAsync(
                        orderId: orderId,
                        orderNumber: orderNumber,
                        title: productTitle,
                        amountMinor: totalAmountMinor,
                        currency: currency,
                        customerName: customerName,
                        customerEmail: customerEmail,
                        checkoutUrl: checkoutUrl,
                        googleFormResponseId: responseId,
                        ct: detachedCts.Token).ConfigureAwait(false);

                    if (sendResult.Success)
                    {
                        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "[GOOGLE-FORM-SMTP] Outbound payment invitation email dispatched successfully for Order {OrderNumber} in {ElapsedMs}ms",
                                orderNumber, sendResult.LatencyMs);
                        }
                    }
                    else
                    {
                        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning(
                                "[GOOGLE-FORM-SMTP] Outbound payment invitation email failed for Order {OrderNumber}: {Error}",
                                orderNumber, sendResult.ErrorDetail ?? sendResult.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_logger is not null && _logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(ex,
                            "[GOOGLE-FORM-SMTP] Exception occurred during outbound payment invitation email dispatch for Order {OrderNumber}",
                            orderNumber);
                    }
                }
            }, CancellationToken.None);
            emailDispatched = true;
            emailMessage = "Dispatched via background SMTP worker.";
        }

        var submission = new GoogleFormSubmission(
            id: Guid.NewGuid().ToString("D"),
            responseId: responseId,
            customerName: customerName,
            customerEmail: customerEmail,
            customerPhone: customerPhone,
            itemReference: productTitle,
            quantity: quantity,
            amount: Money.Php(totalAmountMinor),
            paymentMethod: "hosted_checkout",
            notes: null,
            studentId: null,
            instructorCredit: null,
            bookTitle: productTitle,
            submittedAtUtc: submittedAt,
            isDuplicate: !createdNew);

        submission.AttachOrder(orderId, orderNumber, checkoutUrl, paymentAttemptId, providerPaymentId);
        await _store.SaveSubmissionAsync(submission, ct).ConfigureAwait(false);

        return new SubmitGoogleFormResponse(
            Ok: true,
            OrderId: orderId,
            OrderNumber: orderNumber,
            ResponseId: responseId,
            Status: status,
            CreatedNew: createdNew,
            EmailDispatched: emailDispatched,
            EmailMessage: emailMessage,
            CreatedAt: submittedAt);
    }

    public async Task<Result<GoogleFormSubmissionResponse>> HandleAsync(
        SubmitGoogleFormCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        GoogleFormSubmissionRequest req = command.Request;

        string cleanProvided = (command.ProvidedSecret ?? req.Secret ?? req.SecretKey ?? string.Empty).Trim().Trim('"', '\'');
        if (!_secretVerifier.IsValid(cleanProvided))
        {
            return Result.Failure<GoogleFormSubmissionResponse>(
                Error.Unauthorized("GoogleForms.Unauthorized", "Missing or invalid X-Google-Forms-Secret header."));
        }

        if (string.IsNullOrWhiteSpace(req.ResponseId) ||
            string.IsNullOrWhiteSpace(req.CustomerEmail) ||
            string.IsNullOrWhiteSpace(req.CustomerName))
        {
            return Result.Failure<GoogleFormSubmissionResponse>(
                Error.Validation("GoogleForms.InvalidPayload", "response_id, customer_name and customer_email are required."));
        }

        string itemReference = FirstNonEmpty(req.ItemReference, req.ReferenceCode, req.BookTitle) ?? "CPA Reviewer Volume 1";
        int quantity = Math.Max(1, req.Quantity.GetValueOrDefault(1));

        GoogleFormSubmission? existing = await _store
            .GetByResponseIdAsync(req.ResponseId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(new GoogleFormSubmissionResponse(
                Status: "success",
                Message: "Duplicate submission returned from cache.",
                OrderId: existing.OrderId ?? existing.Id,
                OrderNumber: existing.OrderNumber ?? $"ORD-{existing.Id[..8]}",
                OrderTitle: existing.ItemReference,
                AmountMinor: existing.Amount.AmountMinor,
                AmountPhp: FormatCentavosAsPhp(existing.Amount.AmountMinor),
                Currency: existing.Amount.CurrencyCode,
                CheckoutUrl: existing.CheckoutUrl ?? $"/checkout?order_id={existing.OrderId ?? existing.Id}",
                PaymentAttemptId: existing.PaymentAttemptId ?? $"att_{existing.Id[..8]}",
                ProviderPaymentId: existing.ProviderPaymentId,
                CustomerEmail: existing.CustomerEmail,
                CustomerName: existing.CustomerName,
                IsDuplicate: true));
        }

        if (_orderBridge is not null)
        {
            GoogleFormOrderCreationResult? durable = await _orderBridge
                .FindExistingByResponseIdAsync(req.ResponseId, cancellationToken)
                .ConfigureAwait(false);

            if (durable is not null)
            {
                return Result.Success(ToResponse(durable, req.CustomerEmail, req.CustomerName, isDuplicate: true,
                    message: "Duplicate submission returned from order repository."));
            }
        }

        DateTimeOffset now = _clock.UtcNow;
        string id = Guid.NewGuid().ToString("D");

        string orderId;
        string orderNumber;
        string orderTitle;
        string checkoutUrl;
        string paymentAttemptId;
        string? providerPaymentId;
        Money grossMoney;

        if (_orderBridge is not null)
        {
            GoogleFormOrderCreationResult created = await _orderBridge.CreateOrderAndInitiateCheckoutAsync(
                new GoogleFormOrderCreationRequest(
                    ResponseId: req.ResponseId,
                    SheetRowIndex: req.SheetRowIndex ?? req.SheetRow,
                    CustomerName: req.CustomerName.Trim(),
                    CustomerEmail: req.CustomerEmail.Trim(),
                    CustomerPhone: req.CustomerPhone,
                    ItemReference: itemReference,
                    Category: string.IsNullOrWhiteSpace(req.Category) ? "product_order" : req.Category.Trim(),
                    Quantity: quantity,
                    AmountMinor: req.AmountMinor,
                    PaymentMethod: string.IsNullOrWhiteSpace(req.PaymentMethod) ? "hosted_checkout" : req.PaymentMethod.Trim(),
                    Title: req.Title,
                    Notes: req.Notes,
                    StudentId: req.StudentId,
                    InstructorCredit: req.InstructorCredit,
                    BookTitle: req.BookTitle),
                cancellationToken).ConfigureAwait(false);

            orderId = created.OrderId;
            orderNumber = created.OrderNumber;
            orderTitle = created.OrderTitle;
            checkoutUrl = created.CheckoutUrl;
            paymentAttemptId = created.PaymentAttemptId;
            providerPaymentId = created.ProviderPaymentId;
            grossMoney = new Money(created.AmountMinor, created.Currency);
        }
        else
        {
            long amountMinor = req.AmountMinor ?? (quantity * 850_00L);
            grossMoney = Money.Php(amountMinor);
            orderId = $"ord_{id[..8]}";
            orderNumber = $"ORD-{now:yyyyMM}-{id[..6].ToUpperInvariant()}";
            orderTitle = itemReference;
            checkoutUrl = $"/checkout?order_id={orderId}";
            paymentAttemptId = $"att_{id[..8]}";
            providerPaymentId = null;
        }

        var submission = new GoogleFormSubmission(
            id: id,
            responseId: req.ResponseId,
            customerName: req.CustomerName,
            customerEmail: req.CustomerEmail,
            customerPhone: req.CustomerPhone,
            itemReference: itemReference,
            quantity: quantity,
            amount: grossMoney,
            paymentMethod: req.PaymentMethod ?? "hosted_checkout",
            notes: req.Notes,
            studentId: req.StudentId,
            instructorCredit: req.InstructorCredit,
            bookTitle: req.BookTitle,
            submittedAtUtc: now,
            isDuplicate: false);

        submission.AttachOrder(orderId, orderNumber, checkoutUrl, paymentAttemptId, providerPaymentId);
        await _store.SaveSubmissionAsync(submission, cancellationToken).ConfigureAwait(false);

        var row = new object?[]
        {
            now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            submission.ResponseId,
            submission.CustomerName,
            submission.CustomerEmail,
            submission.CustomerPhone,
            submission.ItemReference,
            submission.Quantity,
            FormatCentavosAsPhp(submission.Amount.AmountMinor),
            submission.PaymentMethod,
            "pending",
            submission.OrderId,
            submission.OrderNumber,
            submission.CheckoutUrl,
            submission.StudentId,
            submission.InstructorCredit,
            submission.BookTitle,
            submission.Notes,
            now.ToString("O", CultureInfo.InvariantCulture)
        };

        await _sheetsClient.AppendRowAsync(row, cancellationToken).ConfigureAwait(false);

        return Result.Success(new GoogleFormSubmissionResponse(
            Status: "created",
            Message: "Google Form order created and checkout session initiated.",
            OrderId: orderId,
            OrderNumber: orderNumber,
            OrderTitle: orderTitle,
            AmountMinor: grossMoney.AmountMinor,
            AmountPhp: FormatCentavosAsPhp(grossMoney.AmountMinor),
            Currency: grossMoney.CurrencyCode,
            CheckoutUrl: checkoutUrl,
            PaymentAttemptId: paymentAttemptId,
            ProviderPaymentId: providerPaymentId,
            CustomerEmail: submission.CustomerEmail,
            CustomerName: submission.CustomerName,
            IsDuplicate: false));
    }

    private static DateTimeOffset ParseHistoricalTimestamp(string? rawTimestamp)
    {
        if (string.IsNullOrWhiteSpace(rawTimestamp))
        {
            return DateTimeOffset.UtcNow;
        }

        string trimmed = rawTimestamp.Trim();

        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dto))
        {
            return dto;
        }

        string[] formats =
        [
            "M/d/yyyy H:mm:ss",
            "M/d/yyyy HH:mm:ss",
            "MM/dd/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ssZ"
        ];

        if (DateTime.TryParseExact(
                trimmed,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime dt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }

        return DateTimeOffset.UtcNow;
    }

    private static long ResolveAmountMinor(long? amountMinor, decimal? amountMajor, string productCode, int quantity)
    {
        if (amountMinor is > 0)
        {
            return amountMinor.Value;
        }

        if (amountMajor is > 0m)
        {
            return (long)Math.Round(amountMajor.Value * 100m, MidpointRounding.AwayFromZero);
        }

        long unitDefault = productCode.ToUpperInvariant() switch
        {
            var c when c.Contains("CPA", StringComparison.OrdinalIgnoreCase) => 1450_00L,
            var c when c.Contains("COST", StringComparison.OrdinalIgnoreCase) => 1850_00L,
            _ => 1850_00L
        };

        return unitDefault * Math.Max(1, quantity);
    }

    private static string NormalizeOrderStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.Trim().ToLowerInvariant() switch
        {
            "paid" or "succeeded" or "completed" => "paid",
            "cancelled" or "canceled" or "void" => "cancelled",
            "failed" or "expired" => "failed",
            _ => "pending"
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static GoogleFormSubmissionResponse ToResponse(
        GoogleFormOrderCreationResult r,
        string customerEmail,
        string customerName,
        bool isDuplicate,
        string message) =>
        new(
            Status: isDuplicate ? "success" : "created",
            Message: message,
            OrderId: r.OrderId,
            OrderNumber: r.OrderNumber,
            OrderTitle: r.OrderTitle,
            AmountMinor: r.AmountMinor,
            AmountPhp: FormatCentavosAsPhp(r.AmountMinor),
            Currency: r.Currency,
            CheckoutUrl: r.CheckoutUrl,
            PaymentAttemptId: r.PaymentAttemptId,
            ProviderPaymentId: r.ProviderPaymentId,
            CustomerEmail: customerEmail,
            CustomerName: customerName,
            IsDuplicate: isDuplicate);

    private static string FormatCentavosAsPhp(long centavos)
    {
        long whole = Math.Abs(centavos) / 100;
        long remainder = Math.Abs(centavos) % 100;
        string sign = centavos < 0 ? "-" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{whole:N0}.{remainder:D2}");
    }
}
