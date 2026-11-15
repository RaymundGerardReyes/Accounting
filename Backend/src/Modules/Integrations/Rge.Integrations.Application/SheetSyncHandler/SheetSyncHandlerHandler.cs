using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Behaviors;
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
    private readonly IApplicationCache? _cache;
    private readonly ILogger<SheetSyncHandlerHandler> _logger;

    public SheetSyncHandlerHandler(
        IGoogleSheetsClient sheetsClient,
        IGoogleFormsStore formsStore,
        IGoogleFormsSubmissionStore submissionStore,
        IGoogleFormOrderBridge orderBridge,
        ILogger<SheetSyncHandlerHandler> logger)
        : this(sheetsClient, formsStore, submissionStore, orderBridge, cache: null, logger)
    {
    }

    public SheetSyncHandlerHandler(
        IGoogleSheetsClient sheetsClient,
        IGoogleFormsStore formsStore,
        IGoogleFormsSubmissionStore submissionStore,
        IGoogleFormOrderBridge orderBridge,
        IApplicationCache? cache,
        ILogger<SheetSyncHandlerHandler> logger)
    {
        _sheetsClient = sheetsClient ?? throw new ArgumentNullException(nameof(sheetsClient));
        _formsStore = formsStore ?? throw new ArgumentNullException(nameof(formsStore));
        _submissionStore = submissionStore ?? throw new ArgumentNullException(nameof(submissionStore));
        _orderBridge = orderBridge ?? throw new ArgumentNullException(nameof(orderBridge));
        _cache = cache;
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

        // 1. Active pull from Google Sheets (or CSV export fallback)
        try
        {
            var sheetRows = await _sheetsClient
                .FetchSheetSubmissionsAsync(command.SpreadsheetId, cancellationToken)
                .ConfigureAwait(false);

            if (sheetRows is { Count: > 0 })
            {
                pulledCount = sheetRows.Count;
                foreach (var submission in sheetRows)
                {
                    await _formsStore.UpsertSubmissionAsync(submission, cancellationToken).ConfigureAwait(false);
                    await _submissionStore.UpsertAsync(submission, cancellationToken).ConfigureAwait(false);
                    storedCount++;

                    try
                    {
                        var bridgeResult = await _orderBridge
                            .EnsureOrderForSubmissionAsync(submission, cancellationToken)
                            .ConfigureAwait(false);

                        if (bridgeResult.IsSuccess)
                        {
                            bridgedCount++;
                        }
                    }
                    catch (Exception bridgeEx)
                    {
                        _logger.LogWarning(bridgeEx, "Failed bridging sheet submission {ResponseId} into orders ledger.", submission.ResponseId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live Google Sheets pull skipped or unconfigured; proceeding with PostgreSQL store reconciliation.");
        }

        // 2. Bridge only pending/unbridged submissions instead of looping through all historical paid orders
        bridgedCount += await _orderBridge
            .BridgePendingSubmissionsAsync(cancellationToken)
            .ConfigureAwait(false);

        // 3. Invalidate caches so next queries reflect the freshly synced submissions and orders
        if (_cache is not null)
        {
            await _cache.InvalidateByTagAsync("orders:", cancellationToken).ConfigureAwait(false);
            await _cache.InvalidateByTagAsync("google-forms:", cancellationToken).ConfigureAwait(false);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "[GOOGLE-SHEETS-SYNC] Sync finished. PulledFromSheet={Pulled}, StoredSubmissions={Stored}, BridgedOrders={Bridged}",
                pulledCount,
                storedCount,
                bridgedCount);
        }

        return Result.Success(new SheetSyncResultDto(
            PulledFromSheet: pulledCount,
            StoredSubmissions: storedCount,
            BridgedOrders: bridgedCount,
            SyncedAtUtc: DateTimeOffset.UtcNow));
    }
}
