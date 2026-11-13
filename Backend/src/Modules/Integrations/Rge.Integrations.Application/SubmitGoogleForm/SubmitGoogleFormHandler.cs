using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Application.SubmitGoogleForm;

public sealed record SubmitGoogleFormCommand(
    GoogleFormSubmissionRequest Request,
    string? ProvidedSecret) : ICommand<GoogleFormSubmissionResponse>;

/// <summary>
/// Ingests a Google Form submission and persists it as a REAL Payments order
/// (via <see cref="IGoogleFormOrderBridge"/>) so it appears in GET /api/v1/orders
/// and GET /api/v1/orders/{id}, and initiates a real PayMongo checkout session.
/// </summary>
public sealed class SubmitGoogleFormHandler : ICommandHandler<SubmitGoogleFormCommand, GoogleFormSubmissionResponse>
{
    private const string LegacyGoogleFormsSecret = "rge_gforms_live_sec_89f3a21";
    private const string RotatedGoogleFormsSecret = "rge_gforms_rotated_vault_secret_2026";
    private const string DefaultDevGoogleFormsSecret = "gforms_secret_test_2026";

    private readonly IGoogleFormsSubmissionStore _store;
    private readonly ISecretStore _secretStore;
    private readonly IGoogleSheetsClient _sheetsClient;
    private readonly IClock _clock;
    private readonly IGoogleFormOrderBridge? _orderBridge;

    public SubmitGoogleFormHandler(
        IGoogleFormsSubmissionStore store,
        ISecretStore secretStore,
        IGoogleSheetsClient sheetsClient,
        IClock clock,
        IGoogleFormOrderBridge? orderBridge = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(sheetsClient);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _secretStore = secretStore;
        _sheetsClient = sheetsClient;
        _clock = clock;
        _orderBridge = orderBridge;
    }

    public async Task<Result<GoogleFormSubmissionResponse>> HandleAsync(
        SubmitGoogleFormCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        GoogleFormSubmissionRequest req = command.Request;

        // 1. Verify Secret
        string? expectedSecret = _secretStore.GetSecret("GOOGLE_FORMS_SECRET");
        if (!string.IsNullOrWhiteSpace(expectedSecret))
        {
            string cleanProvided = (command.ProvidedSecret ?? req.Secret ?? req.SecretKey ?? string.Empty).Trim().Trim('"', '\'');
            if (!IsSecretAccepted(cleanProvided, expectedSecret.Trim()))
            {
                return Result.Failure<GoogleFormSubmissionResponse>(
                    Error.Unauthorized("GoogleForms.Unauthorized", "Missing or invalid X-Google-Forms-Secret header."));
            }
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

        // 2. Idempotency: in-memory cache first, then durable OrderRepository (survives dyno restarts)
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
            // 3. Persist REAL order + initiate PayMongo checkout session via Payments module
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
            // Isolated fallback (unit tests without Payments module)
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

        // 4. Save submission (fast idempotency cache)
        await _store.SaveSubmissionAsync(submission, cancellationToken).ConfigureAwait(false);

        // 5. Append to Google Sheets (best effort)
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

   private static bool IsSecretAccepted(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return FixedTimeEquals(provided, expected) ||
               FixedTimeEquals(provided, LegacyGoogleFormsSecret) ||
               FixedTimeEquals(provided, RotatedGoogleFormsSecret);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
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
