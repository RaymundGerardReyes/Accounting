using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.GeneralLedger;
using Rge.Finance.Application.SecurityGuards;
using Rge.Finance.Contracts;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables;

public sealed record RegisterOfficialPayableCommand(
    Guid PayableId,
    Guid VendorId,
    bool IsVendorVerified,
    bool HasPendingVendorBankChange,
    string RawInvoiceNumber,
    DateOnly InvoiceDate,
    Guid PurchaseRequestId,
    string ReceivingOrAcceptanceReference,
    long PoApprovedAmountCentavos,
    long ReceivedAcceptedAmountCentavos,
    long GrossInvoiceAmountCentavos,
    string ExpenseAccountCode,
    string ExpenseAccountName,
    bool IsVatRegisteredSupplier,
    bool HasSubstantiatedPrincipalVatInvoice,
    int EwtRateBasisPoints,
    string DepartmentId,
    string CostCenterId,
    string PolicyVersionId) : ICommand<Guid>;

public sealed class RegisterOfficialPayableHandler(
    ISubledgerGlPostingService glPostingService) :
    ICommandHandler<RegisterOfficialPayableCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        RegisterOfficialPayableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.IsVendorVerified)
            return Invalid("Payables.UnverifiedVendor", "Cannot register a payable for an unverified vendor.");
        if (command.HasPendingVendorBankChange)
            return Invalid("Payables.PendingVendorBankChange", "Cannot register a payable while a vendor bank change is unresolved.");
        if (string.IsNullOrWhiteSpace(command.ReceivingOrAcceptanceReference))
            return Invalid("Payables.MissingReceivingEvidence", "Three-way match requires receiving or acceptance evidence.");
        if (command.GrossInvoiceAmountCentavos <= 0 ||
            command.GrossInvoiceAmountCentavos > command.PoApprovedAmountCentavos ||
            command.GrossInvoiceAmountCentavos != command.ReceivedAcceptedAmountCentavos)
        {
            return Invalid("Payables.ThreeWayMatchFailed", "Purchase order, receiving evidence, and invoice totals must match.");
        }
        if (command.EwtRateBasisPoints is not (0 or 100 or 200))
            return Invalid("Payables.InvalidEwtRate", "EWT must be 0, 100, or 200 basis points.");

        NormalizedInvoiceNumber invoice;
        try
        {
            invoice = new NormalizedInvoiceNumber(command.RawInvoiceNumber);
        }
        catch (ArgumentException exception)
        {
            return Invalid("Payables.InvalidInvoiceNumber", exception.Message);
        }

        (long netTaxBase, long inputVat, string vatProfile) =
            SplitVat(command.GrossInvoiceAmountCentavos, command.IsVatRegisteredSupplier &&
                command.HasSubstantiatedPrincipalVatInvoice);
        long ewt = (long)Math.Round(
            (netTaxBase * command.EwtRateBasisPoints) / 10_000m,
            MidpointRounding.AwayFromZero);
        long netPayable = command.GrossInvoiceAmountCentavos - ewt;

        var lines = new List<GlJournalLineRecord>
        {
            Line(command.ExpenseAccountCode, command.ExpenseAccountName, netTaxBase, 0,
                command.DepartmentId, command.CostCenterId, vatProfile,
                $"3-way matched invoice {invoice.NormalizedValue} ({command.ReceivingOrAcceptanceReference})")
        };
        if (inputVat > 0)
        {
            lines.Add(Line("1160", "Creditable Input VAT (12%)", inputVat, 0,
                command.DepartmentId, command.CostCenterId, "VAT_12_INCL",
                $"Input VAT on invoice {invoice.NormalizedValue}"));
        }
        if (ewt > 0)
        {
            lines.Add(Line("2120", "Expanded Withholding Tax (EWT) Payable", 0, ewt,
                "DEPT-FIN", "CC-CORP-00",
                command.EwtRateBasisPoints == 100 ? "EWT_1_GOODS" : "EWT_2_SERVICES",
                $"EWT on invoice {invoice.NormalizedValue}"));
        }
        lines.Add(Line("2010", "Accounts Payable - Trade", 0, netPayable,
            "DEPT-FIN", "CC-CORP-00", "NONE",
            $"Trade payable for invoice {invoice.NormalizedValue}"));

        Result<GlJournalBatchRecord> result = await glPostingService.PostAutomaticSubledgerBatchAsync(
            "AccountPayable", command.PayableId.ToString("D"), invoice.NormalizedValue,
            command.InvoiceDate, command.PolicyVersionId, "BIR-EOPT-RR7-2024",
            $"Event D: AP invoice {invoice.NormalizedValue} three-way matched",
            lines, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(result.Value.JournalId)
            : Result.Failure<Guid>(result.Error);
    }

    private static (long NetTaxBase, long InputVat, string Profile) SplitVat(long gross, bool vatQualified)
    {
        if (!vatQualified) return (gross, 0, "NON_VAT_3_PCT");
        long net = (long)Math.Round((gross * 100m) / 112m, MidpointRounding.AwayFromZero);
        return (net, gross - net, "VAT_12_INCL");
    }

    private static GlJournalLineRecord Line(
        string accountCode, string accountName, long debit, long credit,
        string departmentId, string costCenterId, string taxProfile, string description) =>
        new(Guid.NewGuid(), Guid.Empty, 0, accountCode, accountName, debit, credit,
            departmentId, costCenterId, null, taxProfile, description);

    private static Result<Guid> Invalid(string code, string message) =>
        Result.Failure<Guid>(Error.Validation(code, message));
}

public sealed record FrozenBeneficiaryBankSnapshot(
    Guid VendorId,
    string BankName,
    string MaskedAccountNumber,
    string SnapshotSha256Hash)
{
    public static FrozenBeneficiaryBankSnapshot Capture(
        Guid vendorId, string bankName, string maskedAccountNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bankName);
        ArgumentException.ThrowIfNullOrWhiteSpace(maskedAccountNumber);
        string payload = $"{vendorId:D}|{bankName.Trim()}|{maskedAccountNumber.Trim()}";
        return new FrozenBeneficiaryBankSnapshot(
            vendorId, bankName.Trim(), maskedAccountNumber.Trim(), Sha256(payload));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record ExecuteOfficialPayablePaymentCommand(
    Guid PaymentRecordId,
    Guid PayableId,
    string InvoiceNumber,
    long NetPaymentAmountCentavos,
    string PreparedByActorId,
    string VerifiedByActorId,
    FrozenBeneficiaryBankSnapshot FrozenSnapshotAtPreparation,
    string CurrentVendorBankName,
    string CurrentVendorMaskedAccountNumber,
    bool HasUnresolvedVendorBankChangeRequest,
    string BankReferenceNumber,
    DateOnly SettlementDate,
    string PolicyVersionId) : ICommand<Guid>;

public sealed class ExecuteOfficialPayablePaymentHandler(
    ICurrentActor currentActor,
    ISodGuard sodGuard,
    ISubledgerGlPostingService glPostingService) :
    ICommandHandler<ExecuteOfficialPayablePaymentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        ExecuteOfficialPayablePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result sod = sodGuard.CheckSelfApproval(command.PreparedByActorId, command.VerifiedByActorId);
        if (sod.IsFailure) return Result.Failure<Guid>(sod.Error);
        sod = sodGuard.CheckSelfApproval(command.PreparedByActorId, currentActor.ActorId);
        if (sod.IsFailure) return Result.Failure<Guid>(sod.Error);
        sod = sodGuard.CheckSelfApproval(command.VerifiedByActorId, currentActor.ActorId);
        if (sod.IsFailure) return Result.Failure<Guid>(sod.Error);
        if (command.HasUnresolvedVendorBankChangeRequest)
            return Invalid("Payables.PendingVendorBankChange", "Payment execution is blocked by a pending bank change.");

        FrozenBeneficiaryBankSnapshot live = FrozenBeneficiaryBankSnapshot.Capture(
            command.FrozenSnapshotAtPreparation.VendorId,
            command.CurrentVendorBankName,
            command.CurrentVendorMaskedAccountNumber);
        byte[] expected = Encoding.UTF8.GetBytes(command.FrozenSnapshotAtPreparation.SnapshotSha256Hash);
        byte[] actual = Encoding.UTF8.GetBytes(live.SnapshotSha256Hash);
        if (expected.Length != actual.Length ||
            !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return Invalid("Payables.BeneficiaryBankSnapshotMismatch",
                "Vendor bank details changed after payment preparation.");
        }
        if (command.NetPaymentAmountCentavos <= 0 || string.IsNullOrWhiteSpace(command.BankReferenceNumber))
            return Invalid("Payables.InvalidPayment", "Payment amount and bank reference are required.");

        var lines = new[]
        {
            Line("2010", "Accounts Payable - Trade", command.NetPaymentAmountCentavos, 0,
                "DEPT-FIN", "CC-CORP-00", "NONE", $"AP settlement for invoice {command.InvoiceNumber}"),
            Line("1010", "Cash in Bank - Operating PHP", 0, command.NetPaymentAmountCentavos,
                "DEPT-TREASURY", "CC-CORP-00", "NONE", $"Bank payout {command.BankReferenceNumber}")
        };
        Result<GlJournalBatchRecord> result = await glPostingService.PostAutomaticSubledgerBatchAsync(
            "Payment", command.PaymentRecordId.ToString("D"), command.BankReferenceNumber,
            command.SettlementDate, command.PolicyVersionId, "BIR-EOPT-RR7-2024",
            $"Event E: AP payment executed for invoice {command.InvoiceNumber}",
            lines, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(result.Value.JournalId)
            : Result.Failure<Guid>(result.Error);
    }

    private static GlJournalLineRecord Line(
        string accountCode, string accountName, long debit, long credit,
        string departmentId, string costCenterId, string taxProfile, string description) =>
        new(Guid.NewGuid(), Guid.Empty, 0, accountCode, accountName, debit, credit,
            departmentId, costCenterId, null, taxProfile, description);

    private static Result<Guid> Invalid(string code, string message) =>
        Result.Failure<Guid>(Error.Validation(code, message));
}
