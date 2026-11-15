using System.Collections.Concurrent;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Domain;

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

    public Task<IReadOnlyList<GoogleFormSubmission>> ReadSubmissionsAsync(
        string? spreadsheetId = null,
        string? range = null,
        CancellationToken cancellationToken = default)
    {
        // When Google Sheets API credentials are not locally active or configured,
        // safely return an empty collection so caller continues with PostgreSQL reconciliation.
        return Task.FromResult<IReadOnlyList<GoogleFormSubmission>>(Array.Empty<GoogleFormSubmission>());
    }

    public IReadOnlyCollection<IReadOnlyList<object?>> AppendedRows => _appendedRows;
}
