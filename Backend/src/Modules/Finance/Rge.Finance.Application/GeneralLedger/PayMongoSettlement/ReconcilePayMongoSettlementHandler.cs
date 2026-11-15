using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;

namespace Rge.Finance.Application.GeneralLedger.PayMongoSettlement;

public sealed record RecordCustomerPaymentCaptureCommand(
    Guid OrderId,
    string OrderNumber,
    string PayMongoPaymentId,
    long GrossPaymentCentavos,
    bool IsVatRegisteredSale,
    DateOnly CaptureDate,
    string DepartmentId,
    string CostCenterId,
    string PolicyVersionId) : ICommand<Guid>;

public sealed record ReconcilePayMongoBankSettlementCommand(
    Guid SettlementBatchId,
    string PayMongoPaymentId,
    string BankSettlementReference,
    long GrossPaymentCentavos,
    long MerchantFeeCentavos,
    long NetBankSettlementCentavos,
    DateOnly SettlementDate,
    string DepartmentId,
    string CostCenterId,
    string PolicyVersionId) : ICommand<Guid>;

public sealed record RecordCustomerPaymentRefundCommand(
    Guid RefundId,
    Guid OriginalOrderId,
    string OriginalOrderNumber,
    string CreditNoteNumber,
    string PayMongoRefundId,
    long GrossRefundCentavos,
    bool WasVatRegisteredSale,
    DateOnly RefundDate,
    string DepartmentId,
    string CostCenterId,
    string PolicyVersionId) : ICommand<Guid>;

public sealed class ReconcilePayMongoSettlementHandler(
    ISubledgerGlPostingService glPostingService) :
    ICommandHandler<RecordCustomerPaymentCaptureCommand, Guid>,
    ICommandHandler<ReconcilePayMongoBankSettlementCommand, Guid>,
    ICommandHandler<RecordCustomerPaymentRefundCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        RecordCustomerPaymentCaptureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.GrossPaymentCentavos <= 0)
            return Invalid("PayMongo.InvalidGrossAmount", "Gross customer payment must be greater than zero centavos.");

        (long revenue, long vat, string taxProfile) = SplitVat(command.GrossPaymentCentavos, command.IsVatRegisteredSale);
        var lines = new List<GlJournalLineRecord>
        {
            Line("1050", "Payment Gateway Clearing - PayMongo", command.GrossPaymentCentavos, 0,
                command.DepartmentId, command.CostCenterId, "NONE",
                $"PayMongo capture {command.PayMongoPaymentId} for Order {command.OrderNumber}"),
            Line("4010", "Sales & Operating Revenue", 0, revenue,
                command.DepartmentId, command.CostCenterId, taxProfile,
                $"Order {command.OrderNumber} recognized revenue")
        };
        if (vat > 0)
        {
            lines.Add(Line("2130", "Output VAT Payable (12%)", 0, vat,
                "DEPT-FIN", "CC-CORP-00", "VAT_12_INCL",
                $"12% Output VAT on Order {command.OrderNumber}"));
        }

        return await PostAsync(
            command.OrderId, command.OrderNumber, command.CaptureDate, command.PolicyVersionId,
            $"Event F: PayMongo customer payment captured ({command.PayMongoPaymentId})",
            lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<Guid>> HandleAsync(
        ReconcilePayMongoBankSettlementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.GrossPaymentCentavos <= 0 ||
            command.NetBankSettlementCentavos <= 0 ||
            command.MerchantFeeCentavos < 0)
        {
            return Invalid("PayMongo.InvalidSettlementAmounts", "Settlement and fee amounts must be valid centavos.");
        }

        long recomputedGross = checked(command.NetBankSettlementCentavos + command.MerchantFeeCentavos);
        if (recomputedGross != command.GrossPaymentCentavos)
        {
            return Invalid(
                "PayMongo.GrossToNetMismatch",
                $"Net ({command.NetBankSettlementCentavos}) + fee ({command.MerchantFeeCentavos}) must equal gross ({command.GrossPaymentCentavos}).");
        }

        var lines = new List<GlJournalLineRecord>
        {
            Line("1010", "Cash in Bank - Operating PHP", command.NetBankSettlementCentavos, 0,
                "DEPT-TREASURY", "CC-CORP-00", "NONE",
                $"PayMongo net bank settlement Ref {command.BankSettlementReference}"),
            Line("5310", "Merchant Processing Fee Expense", command.MerchantFeeCentavos, 0,
                command.DepartmentId, command.CostCenterId, "NONE",
                $"PayMongo merchant fee deducted on {command.PayMongoPaymentId}"),
            Line("1050", "Payment Gateway Clearing - PayMongo", 0, command.GrossPaymentCentavos,
                command.DepartmentId, command.CostCenterId, "NONE",
                $"Gateway clearing settled for {command.PayMongoPaymentId}")
        };

        return await PostAsync(
            command.SettlementBatchId, command.BankSettlementReference, command.SettlementDate,
            command.PolicyVersionId,
            $"Event G: PayMongo gross-to-net settlement reconciled ({command.BankSettlementReference})",
            lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<Guid>> HandleAsync(
        RecordCustomerPaymentRefundCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.GrossRefundCentavos <= 0)
            return Invalid("PayMongo.InvalidRefundAmount", "Refund amount must be greater than zero centavos.");
        if (command.OriginalOrderId == Guid.Empty)
            return Invalid("PayMongo.MissingOriginalOrder", "A refund must reference the original order.");
        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber))
            return Invalid("PayMongo.MissingCreditNote", "A refund requires a linked credit note reference.");

        (long revenue, long vat, string taxProfile) =
            SplitVat(command.GrossRefundCentavos, command.WasVatRegisteredSale);
        var lines = new List<GlJournalLineRecord>
        {
            Line("4010", "Sales & Operating Revenue", revenue, 0,
                command.DepartmentId, command.CostCenterId, taxProfile,
                $"Revenue reversal per Credit Note {command.CreditNoteNumber} (Order {command.OriginalOrderNumber})")
        };
        if (vat > 0)
        {
            lines.Add(Line("2130", "Output VAT Payable (12%)", vat, 0,
                "DEPT-FIN", "CC-CORP-00", "VAT_12_INCL",
                $"12% Output VAT adjustment per Credit Note {command.CreditNoteNumber}"));
        }
        lines.Add(Line("1050", "Payment Gateway Clearing - PayMongo", 0, command.GrossRefundCentavos,
            command.DepartmentId, command.CostCenterId, "NONE",
            $"PayMongo refund {command.PayMongoRefundId} credited against gateway clearing"));

        return await PostAsync(
            command.RefundId, command.CreditNoteNumber, command.RefundDate, command.PolicyVersionId,
            $"Event H: Customer payment refund {command.PayMongoRefundId} (Credit Note: {command.CreditNoteNumber})",
            lines, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Guid>> PostAsync(
        Guid sourceId,
        string sourceNumber,
        DateOnly accountingDate,
        string policyVersionId,
        string memo,
        IReadOnlyList<GlJournalLineRecord> lines,
        CancellationToken cancellationToken)
    {
        Result<GlJournalBatchRecord> result = await glPostingService.PostAutomaticSubledgerBatchAsync(
            "Payment", sourceId.ToString("D"), sourceNumber, accountingDate, policyVersionId,
            "BIR-EOPT-RR7-2024", memo, lines, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(result.Value.JournalId)
            : Result.Failure<Guid>(result.Error);
    }

    private static (long Revenue, long Vat, string TaxProfile) SplitVat(long gross, bool vatRegistered) =>
        vatRegistered
            ? ((long)Math.Round((gross * 100m) / 112m, MidpointRounding.AwayFromZero),
                gross - (long)Math.Round((gross * 100m) / 112m, MidpointRounding.AwayFromZero),
                "VAT_12_INCL")
            : (gross, 0, "NON_VAT_3_PCT");

    private static GlJournalLineRecord Line(
        string accountCode,
        string accountName,
        long debit,
        long credit,
        string departmentId,
        string costCenterId,
        string taxProfile,
        string description) =>
        new(Guid.NewGuid(), Guid.Empty, 0, accountCode, accountName, debit, credit,
            departmentId, costCenterId, null, taxProfile, description);

    private static Result<Guid> Invalid(string code, string message) =>
        Result.Failure<Guid>(Error.Validation(code, message));
}
