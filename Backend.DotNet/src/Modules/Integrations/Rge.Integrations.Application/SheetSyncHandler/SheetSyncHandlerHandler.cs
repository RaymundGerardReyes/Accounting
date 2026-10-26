using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.SheetSyncHandler;

public sealed record SyncSheetRowCommand(IReadOnlyList<object?> RowData) : ICommand<bool>;

public sealed class SheetSyncHandlerHandler : ICommandHandler<SyncSheetRowCommand, bool>
{
    private readonly IGoogleSheetsClient _sheetsClient;

    public SheetSyncHandlerHandler(IGoogleSheetsClient sheetsClient)
    {
        ArgumentNullException.ThrowIfNull(sheetsClient);
        _sheetsClient = sheetsClient;
    }

    public async Task<Result<bool>> HandleAsync(
        SyncSheetRowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sheetsClient.AppendRowAsync(command.RowData, cancellationToken).ConfigureAwait(false);
        return Result.Success(true);
    }
}

