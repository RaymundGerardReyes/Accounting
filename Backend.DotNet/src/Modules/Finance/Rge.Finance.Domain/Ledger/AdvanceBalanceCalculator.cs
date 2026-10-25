using Rge.BuildingBlocks.SharedKernel.Money;

namespace Rge.Finance.Domain.Ledger;

/// <summary>
/// Dynamically derives the outstanding balance of a CashAdvance from its append-only ledger entries.
/// Stored balances never exist in the database.
/// </summary>
public static class AdvanceBalanceCalculator
{
    public static Money CalculateOutstandingBalance(
        IEnumerable<AdvanceLedgerEntry> entries,
        string currencyCode = "PHP")
    {
        ArgumentNullException.ThrowIfNull(entries);

        long balanceMinor = 0;

        foreach (var entry in entries)
        {
            balanceMinor = entry.EntryType switch
            {
                LedgerEntryType.DisbursementRelease => checked(balanceMinor + entry.AmountMinor),
                LedgerEntryType.ExpenseApplied => checked(balanceMinor - entry.AmountMinor),
                LedgerEntryType.CashReturn => checked(balanceMinor - entry.AmountMinor),
                LedgerEntryType.ReimbursementPayable => checked(balanceMinor - entry.AmountMinor),
                LedgerEntryType.ReimbursementSettled => checked(balanceMinor + entry.AmountMinor),
                LedgerEntryType.Adjustment or LedgerEntryType.Reversal => checked(balanceMinor + entry.SignedAmountMinor),
                _ => balanceMinor
            };
        }

        return new Money(balanceMinor, currencyCode);
    }
}

