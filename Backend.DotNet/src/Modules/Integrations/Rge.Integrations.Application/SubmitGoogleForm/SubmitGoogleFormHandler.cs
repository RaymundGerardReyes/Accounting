using System.Globalization;
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

public sealed class SubmitGoogleFormHandler : ICommandHandler<SubmitGoogleFormCommand, GoogleFormSubmissionResponse>
{
    private readonly IGoogleFormsSubmissionStore _store;
    private readonly ISecretStore _secretStore;
    private readonly IGoogleSheetsClient _sheetsClient;
    private readonly IClock _clock;

    public SubmitGoogleFormHandler(
        IGoogleFormsSubmissionStore store,
        ISecretStore secretStore,
        IGoogleSheetsClient sheetsClient,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(sheetsClient);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _secretStore = secretStore;
        _sheetsClient = sheetsClient;
        _clock = clock;
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
            if (!string.Equals(cleanProvided, expectedSecret, StringComparison.Ordinal))
            {
                return Result.Failure<GoogleFormSubmissionResponse>(
                    Error.Unauthorized("GoogleForms.Unauthorized", "Missing or invalid X-Google-Forms-Secret header."));
            }
        }

        // 2. Check Idempotency (Duplicate Response ID)
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
                AmountPhp: (existing.Amount.AmountMinor / 100.0).ToString("N2", CultureInfo.InvariantCulture),
                Currency: existing.Amount.CurrencyCode,
                CheckoutUrl: existing.CheckoutUrl ?? "https://checkout.paymongo.com/simulated",
                PaymentAttemptId: $"att_{existing.Id[..8]}",
                ProviderPaymentId: null,
                CustomerEmail: existing.CustomerEmail,
                CustomerName: existing.CustomerName,
                IsDuplicate: true));
        }

        // 3. Compute Pricing & Construct Aggregate
        DateTimeOffset now = _clock.UtcNow;
        long amountMinor = req.AmountMinor ?? (req.Quantity.GetValueOrDefault(1) * 850_00L);
        var grossMoney = Money.Php(amountMinor);
        string id = Guid.NewGuid().ToString("D");
        string orderId = $"ord_{id[..8]}";
        string orderNumber = $"ORD-{now:yyyyMM}-{id[..6].ToUpperInvariant()}";
        string checkoutUrl = $"https://checkout.paymongo.com/cs_{id[..12]}";

        var submission = new GoogleFormSubmission(
            id: id,
            responseId: req.ResponseId,
            customerName: req.CustomerName,
            customerEmail: req.CustomerEmail,
            customerPhone: req.CustomerPhone,
            itemReference: req.ItemReference ?? req.ReferenceCode ?? "CPA Reviewer Volume 1",
            quantity: req.Quantity.GetValueOrDefault(1),
            amount: grossMoney,
            paymentMethod: req.PaymentMethod ?? "hosted_checkout",
            notes: req.Notes,
            studentId: req.StudentId,
            instructorCredit: req.InstructorCredit,
            bookTitle: req.BookTitle,
            submittedAtUtc: now,
            isDuplicate: false);

        submission.AttachOrder(orderId, orderNumber, checkoutUrl);

        // 4. Save Submission
        await _store.SaveSubmissionAsync(submission, cancellationToken).ConfigureAwait(false);

        // 5. Append to Google Sheets Async
        var row = new object?[]
        {
            now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            submission.ResponseId,
            submission.CustomerName,
            submission.CustomerEmail,
            submission.CustomerPhone,
            submission.ItemReference,
            submission.Quantity,
            (submission.Amount.AmountMinor / 100.0).ToString("N2", CultureInfo.InvariantCulture),
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
            OrderTitle: submission.ItemReference,
            AmountMinor: submission.Amount.AmountMinor,
            AmountPhp: (submission.Amount.AmountMinor / 100.0).ToString("N2", CultureInfo.InvariantCulture),
            Currency: submission.Amount.CurrencyCode,
            CheckoutUrl: checkoutUrl,
            PaymentAttemptId: $"att_{id[..8]}",
            ProviderPaymentId: null,
            CustomerEmail: submission.CustomerEmail,
            CustomerName: submission.CustomerName,
            IsDuplicate: false));
    }
}

