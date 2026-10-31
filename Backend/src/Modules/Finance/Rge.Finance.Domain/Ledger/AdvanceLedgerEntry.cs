using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Ledger;

/// <summary>
/// Immutable, append-only financial ledger entry for a CashAdvance.
/// </summary>
public sealed class AdvanceLedgerEntry : Entity<string>
{
    public string AdvanceId { get; }
    public string EmployeeId { get; }
    public LedgerEntryType EntryType { get; }
    public long AmountMinor { get; }
    public long SignedAmountMinor { get; }
    public string CurrencyCode { get; }
    public string CreatedByActorId { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    public AdvanceLedgerEntry(
        string id,
        string advanceId,
        string employeeId,
        LedgerEntryType entryType,
        Money amount,
        long signedAmountMinor,
        string createdByActorId,
        DateTimeOffset occurredAtUtc) : base(id)
    {
        AdvanceId = Guard.AgainstNullOrWhiteSpace(advanceId);
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        EntryType = entryType;
        AmountMinor = Guard.AgainstNonPositive(amount.AmountMinor, nameof(amount));
        SignedAmountMinor = signedAmountMinor;
        CurrencyCode = amount.CurrencyCode;
        CreatedByActorId = Guard.AgainstNullOrWhiteSpace(createdByActorId);
        OccurredAtUtc = occurredAtUtc;
    }
}

