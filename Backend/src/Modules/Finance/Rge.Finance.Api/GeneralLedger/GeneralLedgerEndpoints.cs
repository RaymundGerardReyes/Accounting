using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Filters;
using Rge.BuildingBlocks.Api.Http;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.GeneralLedger;
using Rge.Finance.Application.Ports;

namespace Rge.Finance.Api.GeneralLedger;

public sealed class GeneralLedgerEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/finance/gl/accounts", async (
            [FromServices] IGeneralLedgerRepository repository,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<GlChartOfAccountRecord> accounts =
                await repository.GetActiveChartOfAccountsAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(accounts.Select(account => new ChartOfAccountResponse(
                account.AccountCode, account.AccountName, account.AccountType, account.NormalBalance,
                account.DefaultTaxProfile, account.IsActive, account.Description)));
        }).WithTags("Finance General Ledger");

        app.MapGet("/api/v1/finance/gl/journals", async (
            [FromQuery] string period,
            [FromQuery] string? status,
            [FromQuery] string? source_module,
            [FromQuery] int? limit,
            HttpContext httpContext,
            [FromServices] GetGeneralLedgerJournalsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetGeneralLedgerJournalsQuery(period, status, source_module, limit ?? 100);
            var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, journals => Results.Ok(journals.Select(ToResponse).ToList()));
        }).WithTags("Finance General Ledger");

        app.MapGet("/api/v1/finance/gl/trial-balance", async (
            [FromQuery] string period,
            [FromQuery] string? department_id,
            HttpContext httpContext,
            [FromServices] GetGeneralLedgerTrialBalanceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetGeneralLedgerTrialBalanceQuery(period, department_id);
            var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, rows =>
            {
                long periodDebit = rows.Sum(row => row.PeriodDebitCentavos);
                long periodCredit = rows.Sum(row => row.PeriodCreditCentavos);
                long endingDebit = rows.Sum(row => row.EndingDebitCentavos);
                long endingCredit = rows.Sum(row => row.EndingCreditCentavos);
                return Results.Ok(new TrialBalanceSummaryResponse(
                    period,
                    periodDebit == periodCredit,
                    periodDebit,
                    periodCredit,
                    endingDebit,
                    endingCredit,
                    rows.Select(row => new TrialBalanceRowResponse(
                        row.AccountCode, row.AccountName, row.AccountType, row.NormalBalance,
                        row.OpeningDebitCentavos, row.OpeningCreditCentavos,
                        row.PeriodDebitCentavos, row.PeriodCreditCentavos,
                        row.EndingDebitCentavos, row.EndingCreditCentavos)).ToList()));
            });
        }).WithTags("Finance General Ledger");

        app.MapPost("/api/v1/finance/gl/journals", async (
            [FromBody] CreateManualJournalRequest request,
            HttpContext httpContext,
            [FromServices] CreateManualJournalHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateManualJournalCommand(
                request.AccountingDate,
                request.Memo,
                request.Lines.Select(line => new CreateManualJournalLineInput(
                    line.AccountCode, line.DebitCentavos, line.CreditCentavos,
                    line.DepartmentId, line.CostCenterId, line.PurposeCode,
                    line.TaxProfile, line.Description)).ToList());
            var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, journalId =>
                Results.Created($"/api/v1/finance/gl/journals/{journalId}", new { journalId }));
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .AddEndpointFilter<ValidationFilter<CreateManualJournalRequest>>()
        .WithTags("Finance General Ledger");

        app.MapPost("/api/v1/finance/gl/journals/{journalId:guid}/decide", async (
            [FromRoute] Guid journalId,
            [FromQuery] string prepared_by_actor_id,
            [FromBody] DecideJournalApprovalRequest request,
            HttpContext httpContext,
            [FromServices] DecideJournalApprovalHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new DecideJournalApprovalCommand(
                journalId, prepared_by_actor_id, request.Decision, request.Reason);
            var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(httpContext, _ => Results.Ok(new { journalId, status = request.Decision }));
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .AddEndpointFilter<ValidationFilter<DecideJournalApprovalRequest>>()
        .WithTags("Finance General Ledger");
    }

    private static GlJournalEntryResponse ToResponse(GlJournalBatchRecord journal) =>
        new(
            journal.JournalId,
            journal.JournalNumber,
            journal.SourceModule,
            journal.SourceDocumentId,
            journal.SourceDocumentNumber,
            journal.AccountingPeriod,
            journal.AccountingDate,
            journal.PostingStatus,
            journal.PolicyVersionId,
            journal.TaxRuleVersion,
            journal.TotalDebitCentavos,
            journal.TotalCreditCentavos,
            journal.PreparedByActorId,
            journal.PreparedByName,
            journal.VerifiedByActorId,
            journal.VerifiedByName,
            journal.PostedByActorId,
            journal.PostedByName,
            journal.AuditHash,
            journal.Memo,
            journal.Lines.Select(line => new GlJournalLineDto(
                line.LineNumber, line.AccountCode, line.AccountName,
                line.DebitCentavos, line.CreditCentavos, line.DepartmentId,
                line.CostCenterId, line.PurposeCode, line.TaxProfile, line.Description)).ToList());
}
