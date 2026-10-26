using System.Collections.Concurrent;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure;

public sealed class GoogleSheetsClient : IGoogleSheetsClient
{
    private readonly ConcurrentBag<IReadOnlyList<object?>> _appendedRows = new();

    public Task AppendRowAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        _appendedRows.Add(row);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<IReadOnlyList<object?>> AppendedRows => _appendedRows;
}
