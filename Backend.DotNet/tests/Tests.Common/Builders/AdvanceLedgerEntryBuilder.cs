using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Finance.Domain.Ledger;

namespace Rge.Tests.Common.Builders;

/// <summary>
/// Fluent builder for constructing immutable AdvanceLedgerEntry records for balance calculations.
/// </summary>
public sealed class AdvanceLedgerEntryBuilder
{
    private string _id = "entry-001";
    private string _advanceId = "adv-test-001";
    private string _employeeId = "emp-001";
    private LedgerEntryType _entryType = LedgerEntryType.DisbursementRelease;
    private Money _amount = Money.Php(10_000_00);
    private long _signedAmountMinor = 10_000_00;
    private string _createdBy = "finance_officer";
    private DateTimeOffset _occurredAtUtc = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    public AdvanceLedgerEntryBuilder WithAdvanceId(string advanceId)
    {
        _advanceId = advanceId;
        return this;
    }

    public AdvanceLedgerEntryBuilder WithEmployeeId(string employeeId)
    {
        _employeeId = employeeId;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsRelease(Money amount)
    {
        _entryType = LedgerEntryType.DisbursementRelease;
        _amount = amount;
        _signedAmountMinor = amount.AmountMinor;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsExpenseApplied(Money amount)
    {
        _entryType = LedgerEntryType.ExpenseApplied;
        _amount = amount;
        _signedAmountMinor = -amount.AmountMinor;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsCashReturn(Money amount)
    {
        _entryType = LedgerEntryType.CashReturn;
        _amount = amount;
        _signedAmountMinor = -amount.AmountMinor;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsReimbursementPayable(Money amount)
    {
        _entryType = LedgerEntryType.ReimbursementPayable;
        _amount = amount;
        _signedAmountMinor = amount.AmountMinor;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsReimbursementSettled(Money amount)
    {
        _entryType = LedgerEntryType.ReimbursementSettled;
        _amount = amount;
        _signedAmountMinor = -amount.AmountMinor;
        return this;
    }

    public AdvanceLedgerEntryBuilder AsAdjustment(long signedAmountMinor, string currency = "PHP")
    {
        _entryType = LedgerEntryType.Adjustment;
        _amount = new Money(Math.Abs(signedAmountMinor), currency);
        _signedAmountMinor = signedAmountMinor;
        return this;
    }

    public AdvanceLedgerEntry Build()
    {
        return new AdvanceLedgerEntry(
            id: _id,
            advanceId: _advanceId,
            employeeId: _employeeId,
            entryType: _entryType,
            amount: _amount,
            signedAmountMinor: _signedAmountMinor,
            createdByActorId: _createdBy,
            occurredAtUtc: _occurredAtUtc);
    }
}
