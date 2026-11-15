using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;

namespace Rge.Finance.Application.GeneralLedger;

public interface ISubledgerGlPostingService
{
    Task<Result<GlJournalBatchRecord>> PostAutomaticSubledgerBatchAsync(
        string sourceModule,
        string sourceDocumentId,
        string sourceDocumentNumber,
        DateOnly accountingDate,
        string policyVersionId,
        string taxRuleVersion,
        string memo,
        IReadOnlyList<GlJournalLineRecord> lines,
        CancellationToken cancellationToken = default);
}

public sealed class SubledgerGlPostingService(
    IGeneralLedgerRepository glRepository,
    ICurrentActor currentActor,
    IClock clock) : ISubledgerGlPostingService
{
    public async Task<Result<GlJournalBatchRecord>> PostAutomaticSubledgerBatchAsync(
        string sourceModule,
        string sourceDocumentId,
        string sourceDocumentNumber,
        DateOnly accountingDate,
        string policyVersionId,
        string taxRuleVersion,
        string memo,
        IReadOnlyList<GlJournalLineRecord> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count < 2)
        {
            return Result.Failure<GlJournalBatchRecord>(Error.Validation(
                "GeneralLedger.MinLines",
                "Subledger journal requires at least two lines."));
        }

        long debit = 0;
        long credit = 0;
        foreach (GlJournalLineRecord line in lines)
        {
            if (line.DebitCentavos < 0 || line.CreditCentavos < 0 ||
                (line.DebitCentavos > 0 && line.CreditCentavos > 0) ||
                (line.DebitCentavos == 0 && line.CreditCentavos == 0))
            {
                return Result.Failure<GlJournalBatchRecord>(Error.Validation(
                    "GeneralLedger.InvalidLine",
                    $"Journal line {line.LineNumber} must contain exactly one non-negative non-zero side."));
            }

            debit = checked(debit + line.DebitCentavos);
            credit = checked(credit + line.CreditCentavos);
        }

        if (debit <= 0 || debit != credit)
        {
            return Result.Failure<GlJournalBatchRecord>(Error.Validation(
                "GeneralLedger.UnbalancedSubledger",
                $"Subledger event is unbalanced ({debit} debit vs {credit} credit centavos)."));
        }

        Guid journalId = Guid.NewGuid();
        string journalNumber = await glRepository.NextJournalNumberAsync(accountingDate.Year, cancellationToken).ConfigureAwait(false);
        string period = $"{accountingDate.Year:D4}-{accountingDate.Month:D2}";
        DateTimeOffset timestamp = clock.UtcNow;
        var boundLines = lines.Select((line, index) => line with
        {
            JournalId = journalId,
            LineId = line.LineId == Guid.Empty ? Guid.NewGuid() : line.LineId,
            LineNumber = index + 1
        }).ToList();

        string canonical = JsonSerializer.Serialize(new
        {
            journalId,
            journalNumber,
            sourceModule,
            sourceDocumentId,
            sourceDocumentNumber,
            period,
            accountingDate,
            debit,
            credit,
            actorId = currentActor.ActorId,
            timestamp,
            lines = boundLines.Select(line => new
            {
                line.LineNumber,
                line.AccountCode,
                line.DebitCentavos,
                line.CreditCentavos,
                line.DepartmentId,
                line.CostCenterId,
                line.TaxProfile
            })
        });
        string auditHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        var batch = new GlJournalBatchRecord(
            journalId,
            journalNumber,
            sourceModule,
            sourceDocumentId,
            sourceDocumentNumber,
            period,
            accountingDate,
            "Posted",
            policyVersionId,
            taxRuleVersion,
            debit,
            credit,
            currentActor.ActorId,
            currentActor.ActorId,
            "SYSTEM-SUBLEDGER-GUARD",
            "Approved Operational Workflow",
            "SYSTEM-GL-POSTER",
            "Automated Official GL Subledger Poster",
            auditHash,
            memo,
            boundLines);

        await glRepository.InsertJournalBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        return Result.Success(batch);
    }
}
