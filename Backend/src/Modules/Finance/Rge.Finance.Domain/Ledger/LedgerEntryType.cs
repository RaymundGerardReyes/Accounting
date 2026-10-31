namespace Rge.Finance.Domain.Ledger;

/// <summary>
/// Entry types for the append-only AdvanceLedgerEntry table.
/// </summary>
public enum LedgerEntryType
{
    DisbursementRelease = 1,
    ExpenseApplied = 2,
    CashReturn = 3,
    ReimbursementPayable = 4,
    ReimbursementSettled = 5,
    Adjustment = 6,
    Reversal = 7
}

