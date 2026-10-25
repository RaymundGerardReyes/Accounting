using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.CashAdvances.GetById;

public sealed record GetCashAdvanceByIdQuery(string Id) : IQuery<CashAdvanceDetailDto>;

public sealed record CashAdvanceDetailDto(
    CashAdvanceDto Advance,
    long OutstandingBalanceMinor,
    IReadOnlyList<AdvanceLedgerEntryDto> LedgerEntries);

public sealed record AdvanceLedgerEntryDto(
    string Id,
    string EntryType,
    long AmountMinor,
    long SignedAmountMinor,
    string CurrencyCode,
    string CreatedByActorId,
    DateTimeOffset OccurredAtUtc);

public sealed class GetByIdHandler : IQueryHandler<GetCashAdvanceByIdQuery, CashAdvanceDetailDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IAdvanceLedgerStore _ledgerStore;

    public GetByIdHandler(ICashAdvanceRepository repository, IAdvanceLedgerStore ledgerStore)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ledgerStore);

        _repository = repository;
        _ledgerStore = ledgerStore;
    }

    public async Task<Result<CashAdvanceDetailDto>> HandleAsync(
        GetCashAdvanceByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CashAdvance? advance = await _repository.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<CashAdvanceDetailDto>(FinanceErrors.AdvanceNotFound(query.Id));
        }

        IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(advance.Id, cancellationToken).ConfigureAwait(false);
        var balance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, advance.RequestedAmount.CurrencyCode);

        var ledgerDtos = entries.Select(e => new AdvanceLedgerEntryDto(
            Id: e.Id,
            EntryType: e.EntryType.ToString(),
            AmountMinor: e.AmountMinor,
            SignedAmountMinor: e.SignedAmountMinor,
            CurrencyCode: e.CurrencyCode,
            CreatedByActorId: e.CreatedByActorId,
            OccurredAtUtc: e.OccurredAtUtc)).ToList();

        return Result.Success(new CashAdvanceDetailDto(
            Advance: CreateCashAdvanceHandler.MapToDto(advance),
            OutstandingBalanceMinor: balance.AmountMinor,
            LedgerEntries: ledgerDtos));
    }
}
