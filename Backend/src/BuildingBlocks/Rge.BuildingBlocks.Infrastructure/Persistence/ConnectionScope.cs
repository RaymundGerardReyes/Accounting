using System.Data.Common;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>
/// Scoped connection holder that lazily opens a single DbConnection per HTTP request
/// and shares any active DbTransaction across repositories and the Outbox writer.
/// </summary>
public sealed class ConnectionScope : IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSourceFactory? _dataSourceFactory;
    private DbConnection? _connection;
    public DbTransaction? CurrentTransaction { get; internal set; }

    public ConnectionScope(NpgsqlDataSourceFactory? dataSourceFactory = null)
    {
        _dataSourceFactory = dataSourceFactory;
    }

    public async ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_dataSourceFactory is null)
        {
            throw new InvalidOperationException("No database connection factory configured (offline mode).");
        }

        if (_connection is null)
        {
            _connection = await _dataSourceFactory
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return _connection;
    }

    public void Dispose()
    {
        CurrentTransaction?.Dispose();
        CurrentTransaction = null;

        _connection?.Dispose();
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (CurrentTransaction is not null)
        {
            await CurrentTransaction.DisposeAsync().ConfigureAwait(false);
            CurrentTransaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
