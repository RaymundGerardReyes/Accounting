using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Contracts;

namespace Rge.Finance.Application.GeneralLedger;

public sealed record CreateManualJournalLineInput(
    string AccountCode,
    long DebitCentavos,
    long CreditCentavos,
    string DepartmentId,
    string CostCenterId,
    string? PurposeCode,
    string TaxProfile,
    string Description);

public sealed record CreateManualJournalCommand(
    DateOnly AccountingDate,
    string Memo,
    IReadOnlyList<CreateManualJournalLineInput> Lines) : ICommand<Guid>;

public sealed class CreateManualJournalHandler(
    IGeneralLedgerRepository repository,
    ICurrentActor currentActor,
    IClock clock) : ICommandHandler<CreateManualJournalCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateManualJournalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Lines is null || command.Lines.Count < 2)
        {
            return Result.Failure<Guid>(Error.Validation("GeneralLedger.MinimumLines", "A balanced journal requires at least two lines."));
        }

        long debit = 0;
        long credit = 0;
        foreach (CreateManualJournalLineInput line in command.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.AccountCode))
                return Result.Failure<Guid>(Error.Validation("GeneralLedger.MissingAccount", "Every journal line requires an account code."));
            if (line.DebitCentavos < 0 || line.CreditCentavos < 0)
                return Result.Failure<Guid>(Error.Validation("GeneralLedger.NegativeCentavos", "Journal amounts cannot be negative."));
            if (line.DebitCentavos > 0 && line.CreditCentavos > 0)
                return Result.Failure<Guid>(Error.Validation("GeneralLedger.DualSidedLine", "A line cannot contain both debit and credit."));
            if (line.DebitCentavos == 0 && line.CreditCentavos == 0)
                return Result.Failure<Guid>(Error.Validation("GeneralLedger.ZeroLine", "A journal line must not be zero."));

            debit = checked(debit + line.DebitCentavos);
            credit = checked(credit + line.CreditCentavos);
        }

        if (debit <= 0 || debit != credit)
            return Result.Failure<Guid>(Error.Validation("GeneralLedger.UnbalancedBatch", $"Debit ({debit}) must equal credit ({credit}) centavos."));

        Guid journalId = Guid.NewGuid();
        string journalNumber = await repository.NextJournalNumberAsync(command.AccountingDate.Year, cancellationToken).ConfigureAwait(false);
        string period = $"{command.AccountingDate.Year:D4}-{command.AccountingDate.Month:D2}";
        var lines = command.Lines.Select((line, index) => new GlJournalLineRecord(
            Guid.NewGuid(), journalId, index + 1, line.AccountCode, line.AccountCode,
            line.DebitCentavos, line.CreditCentavos, line.DepartmentId, line.CostCenterId,
            line.PurposeCode, line.TaxProfile, line.Description)).ToList();

        string auditHash = GeneralLedgerHash.Compute(new
        {
            journalId,
            journalNumber,
            period,
            command.AccountingDate,
            debit,
            credit,
            actor = currentActor.ActorId,
            timestamp = clock.UtcNow,
            lines
        });

        await repository.InsertJournalBatchAsync(new GlJournalBatchRecord(
            journalId, journalNumber, "ManualAdjustment", journalId.ToString("D"), journalNumber,
            period, command.AccountingDate, "Validated", "MANUAL", "BIR-EOPT-RR7-2024",
            debit, credit, currentActor.ActorId, currentActor.ActorId, null, null, null, null,
            auditHash, command.Memo ?? string.Empty, lines), cancellationToken).ConfigureAwait(false);

        return Result.Success(journalId);
    }
}

public sealed record DecideJournalApprovalCommand(
    Guid JournalId,
    string PreparedByActorId,
    string Decision,
    string? Reason) : ICommand<bool>;

public sealed class DecideJournalApprovalHandler(
    IGeneralLedgerRepository repository,
    ICurrentActor currentActor,
    ISodGuard sodGuard,
    IClock clock) : ICommandHandler<DecideJournalApprovalCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DecideJournalApprovalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result sod = sodGuard.CheckSelfApproval(command.PreparedByActorId, currentActor.ActorId);
        if (sod.IsFailure) return Result.Failure<bool>(sod.Error);

        GlJournalBatchRecord? journal = await repository.GetJournalByIdAsync(command.JournalId, cancellationToken).ConfigureAwait(false);
        if (journal is null)
            return Result.Failure<bool>(Error.NotFound("GeneralLedger.JournalNotFound", $"Journal '{command.JournalId}' was not found."));

        string decision = command.Decision.Trim().ToLowerInvariant();
        string status = decision switch
        {
            "approve" or "approved" => "Approved",
            "post" or "posted" => "Posted",
            "reject" or "rejected" => "Reversed",
            _ => string.Empty
        };
        if (status.Length == 0)
            return Result.Failure<bool>(Error.Validation("GeneralLedger.InvalidDecision", "Decision must be Approve, Post, or Reject."));
        if (journal.PostingStatus == "Posted")
            return Result.Failure<bool>(Error.Conflict("GeneralLedger.PostedImmutable", "Posted journals require a reversing entry."));

        string auditHash = GeneralLedgerHash.Compute(new
        {
            journal.JournalId,
            journal.JournalNumber,
            status,
            actor = currentActor.ActorId,
            timestamp = clock.UtcNow,
            command.Reason
        });
        await repository.UpdateJournalDecisionAsync(command.JournalId, status, currentActor.ActorId, currentActor.ActorId, auditHash, cancellationToken).ConfigureAwait(false);
        return Result.Success(true);
    }
}

public sealed record GetGeneralLedgerJournalsQuery(
    string AccountingPeriod,
    string? PostingStatus = null,
    string? SourceModule = null,
    int Limit = 100) : IQuery<IReadOnlyList<GlJournalBatchRecord>>;

public sealed class GetGeneralLedgerJournalsHandler(IGeneralLedgerRepository repository)
    : IQueryHandler<GetGeneralLedgerJournalsQuery, IReadOnlyList<GlJournalBatchRecord>>
{
    public Task<Result<IReadOnlyList<GlJournalBatchRecord>>> HandleAsync(GetGeneralLedgerJournalsQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    private async Task<Result<IReadOnlyList<GlJournalBatchRecord>>> ExecuteAsync(GetGeneralLedgerJournalsQuery query, CancellationToken cancellationToken)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(query.AccountingPeriod, "^\\d{4}-\\d{2}$"))
            return Result.Failure<IReadOnlyList<GlJournalBatchRecord>>(Error.Validation("GeneralLedger.InvalidPeriod", "Accounting period must use YYYY-MM."));
        IReadOnlyList<GlJournalBatchRecord> rows = await repository.ListJournalBatchesByPeriodAsync(query.AccountingPeriod, query.PostingStatus, query.SourceModule, Math.Clamp(query.Limit, 1, 500), cancellationToken).ConfigureAwait(false);
        return Result.Success(rows);
    }
}

public sealed record GetGeneralLedgerTrialBalanceQuery(string AccountingPeriod, string? DepartmentId = null)
    : IQuery<IReadOnlyList<GlTrialBalanceRowRecord>>;

public sealed class GetGeneralLedgerTrialBalanceHandler(IGeneralLedgerRepository repository)
    : IQueryHandler<GetGeneralLedgerTrialBalanceQuery, IReadOnlyList<GlTrialBalanceRowRecord>>
{
    public async Task<Result<IReadOnlyList<GlTrialBalanceRowRecord>>> HandleAsync(GetGeneralLedgerTrialBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!System.Text.RegularExpressions.Regex.IsMatch(query.AccountingPeriod, "^\\d{4}-\\d{2}$"))
            return Result.Failure<IReadOnlyList<GlTrialBalanceRowRecord>>(Error.Validation("GeneralLedger.InvalidPeriod", "Accounting period must use YYYY-MM."));
        return Result.Success(await repository.GetTrialBalanceAsync(query.AccountingPeriod, query.DepartmentId, cancellationToken).ConfigureAwait(false));
    }
}

public static class GeneralLedgerHash
{
    public static string Compute<T>(T value)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    }
}
