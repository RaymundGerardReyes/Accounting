using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Liquidation;

/// <summary>
/// Value object/entity representing the application of an expense or repayment against an outstanding advance.
/// </summary>
public sealed class SettlementApplication : Entity<string>
{
    public string? ExpenseReportId { get; private set; }
    public string? ExpenseLineId { get; private set; }
    public string? AdvanceId { get; private set; }
    public string EmployeeId { get; private set; }
    public string ApplicationType { get; private set; }
    public Money AppliedAmount { get; private set; }
    public string? LedgerEntryId { get; private set; }
    public string? EvidenceDocumentId { get; private set; }
    public string? BankReference { get; private set; }
    public string AppliedById { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset AppliedAtUtc { get; private set; }

    public SettlementApplication(
        string id,
        string? expenseReportId,
        string? expenseLineId,
        string? advanceId,
        string employeeId,
        string applicationType,
        Money appliedAmount,
        string? ledgerEntryId,
        string? evidenceDocumentId,
        string? bankReference,
        string appliedById,
        string? notes,
        DateTimeOffset appliedAtUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ExpenseReportId = expenseReportId;
        ExpenseLineId = expenseLineId;
        AdvanceId = advanceId;
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        ApplicationType = Guard.AgainstNullOrWhiteSpace(applicationType);
        AppliedAmount = appliedAmount;
        LedgerEntryId = ledgerEntryId;
        EvidenceDocumentId = evidenceDocumentId;
        BankReference = bankReference;
        AppliedById = Guard.AgainstNullOrWhiteSpace(appliedById);
        Notes = notes;
        AppliedAtUtc = appliedAtUtc;
    }

    public static SettlementApplication Create(
        string? expenseReportId,
        string? expenseLineId,
        string? advanceId,
        string employeeId,
        string applicationType,
        Money appliedAmount,
        string? ledgerEntryId,
        string? evidenceDocumentId,
        string? bankReference,
        string appliedById,
        string? notes,
        DateTimeOffset appliedAtUtc)
    {
        return new SettlementApplication(
            id: Guid.NewGuid().ToString("D"),
            expenseReportId: expenseReportId,
            expenseLineId: expenseLineId,
            advanceId: advanceId,
            employeeId: employeeId,
            applicationType: applicationType,
            appliedAmount: appliedAmount,
            ledgerEntryId: ledgerEntryId,
            evidenceDocumentId: evidenceDocumentId,
            bankReference: bankReference,
            appliedById: appliedById,
            notes: notes,
            appliedAtUtc: appliedAtUtc);
    }
}
