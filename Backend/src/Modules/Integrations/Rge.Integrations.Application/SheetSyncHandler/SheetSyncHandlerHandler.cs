using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Application.SheetSyncHandler;

public sealed record SheetSyncCommand(string? SpreadsheetId = null, string? Range = null);

public sealed record SheetSyncResultDto(
    int PulledFromSheet,
    int StoredSubmissions,
    int BridgedOrders,
    DateTimeOffset SyncedAtUtc);

public sealed class SheetSyncHandlerHandler
{
    private readonly IGoogleSheetsClient _sheetsClient;
    private readonly IGoogleFormsStore _formsStore;
    private readonly IGoogleFormsSubmissionStore _submissionStore;
    private readonly IGoogleFormOrderBridge _orderBridge;
    private readonly ILogger<SheetSyncHandlerHandler> _logger;

    public SheetSyncHandlerHandler(
        IGoogleSheetsClient sheetsClient,
        IGoogleFormsStore formsStore,
        IGoogleFormsSubmissionStore submissionStore,
        IGoogleFormOrderBridge orderBridge,
        ILogger<SheetSyncHandlerHandler> logger)
    {
        _sheetsClient = sheetsClient ?? throw new ArgumentNullException(nameof(sheetsClient));
        _formsStore = formsStore ?? throw new ArgumentNullException(nameof(formsStore));
        _submissionStore = submissionStore ?? throw new ArgumentNullException(nameof(submissionStore));
        _orderBridge = orderBridge ?? throw new ArgumentNullException(nameof(orderBridge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SheetSyncResultDto>> HandleAsync(
        SheetSyncCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var pulledCount = 0;
        var storedCount = 0;
        var bridgedCount = 0;

        // 1. Active pull from Google Sheets if configured
        try
        {
            var sheetRows = await _sheetsClient
                .ReadSubmissionsAsync(command.SpreadsheetId, command.Range, cancellationToken)
                .ConfigureAwait(false);

            if (sheetRows is { Count: > 0 })
            {
                pulledCount = sheetRows.Count;
                foreach (var submission in sheetRows)
                {
                    await _submissionStore.UpsertAsync(submission, cancellationToken).ConfigureAwait(false);
                    storedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live Google Sheets pull skipped or unconfigured; proceeding with PostgreSQL store reconciliation.");
        }

        // 2. Ensure every submission in public.google_form_submissions is bridged into public.orders
        var existingSubmissions = await _formsStore
            .GetRecentSubmissionsAsync(250, cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in existingSubmissions)
        {
            try
            {
                var bridgeResult = await _orderBridge
                    .EnsureOrderForSubmissionAsync(item, cancellationToken)
                    .ConfigureAwait(false);

                if (bridgeResult.IsSuccess)
                {
                    bridgedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed bridging submission {ResponseId} into orders ledger.", item.ResponseId);
            }
        }

        return Result.Success(new SheetSyncResultDto(
            PulledFromSheet: pulledCount,
            StoredSubmissions: storedCount,
            BridgedOrders: bridgedCount,
            SyncedAtUtc: DateTimeOffset.UtcNow));
    }
}