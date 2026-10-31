using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.Reporting.AgingBuckets;

public sealed record GetAgingBucketsQuery(string? AsOfDate = null) : IQuery<AgingBucketsDto>;

public sealed record AgingBucketsDto(
    long Current0To30DaysMinor,
    long PastDue31To60DaysMinor,
    long PastDue61To90DaysMinor,
    long Over90DaysMinor,
    long TotalOutstandingMinor);

public sealed class AgingBucketsHandler : IQueryHandler<GetAgingBucketsQuery, AgingBucketsDto>
{
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly IClock _clock;

    public AgingBucketsHandler(
        ICashAdvanceRepository advanceRepository,
        IAdvanceLedgerStore ledgerStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(clock);

        _advanceRepository = advanceRepository;
        _ledgerStore = ledgerStore;
        _clock = clock;
    }

    public async Task<Result<AgingBucketsDto>> HandleAsync(
        GetAgingBucketsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<CashAdvance> advances = await _advanceRepository.ListAsync(null, null, 200, cancellationToken).ConfigureAwait(false);

        DateOnly asOf = query.AsOfDate != null && DateOnly.TryParse(query.AsOfDate, out DateOnly parsed)
            ? parsed
            : DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        long bucketCurrent = 0;
        long bucket31To60 = 0;
        long bucket61To90 = 0;
        long bucketOver90 = 0;
        long totalOutstanding = 0;

        foreach (CashAdvance adv in advances)
        {
            if (adv.PaymentStatus != PaymentStatus.EvidenceVerified)
            {
                continue;
            }

            IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(adv.Id, cancellationToken).ConfigureAwait(false);
            var balance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, adv.RequestedAmount.CurrencyCode);

            if (balance.AmountMinor <= 0)
            {
                continue;
            }

            totalOutstanding += balance.AmountMinor;

            DateOnly dueDate = !string.IsNullOrWhiteSpace(adv.LiquidationDueDate) && DateOnly.TryParse(adv.LiquidationDueDate, out DateOnly parsedDue)
                ? parsedDue
                : asOf;

            int daysPastDue = asOf.DayNumber - dueDate.DayNumber;

            if (daysPastDue <= 30)
            {
                bucketCurrent += balance.AmountMinor;
            }
            else if (daysPastDue <= 60)
            {
                bucket31To60 += balance.AmountMinor;
            }
            else if (daysPastDue <= 90)
            {
                bucket61To90 += balance.AmountMinor;
            }
            else
            {
                bucketOver90 += balance.AmountMinor;
            }
        }

        return Result.Success(new AgingBucketsDto(
            Current0To30DaysMinor: bucketCurrent,
            PastDue31To60DaysMinor: bucket31To60,
            PastDue61To90DaysMinor: bucket61To90,
            Over90DaysMinor: bucketOver90,
            TotalOutstandingMinor: totalOutstanding));
    }
}
