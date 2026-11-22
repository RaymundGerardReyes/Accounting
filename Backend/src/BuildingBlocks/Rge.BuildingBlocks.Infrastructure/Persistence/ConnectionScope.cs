using System.Data;
using System.Data.Common;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Scoped connection holder that lazily opens a single DbConnection per HTTP request
/// and shares any active DbTransaction across repositories and the Outbox writer.
/// Also provides short-lived standalone connections for background workers so pooled
/// connections are never held open across polling cycles.
/// </summary>
public sealed class ConnectionScope : IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSourceFactory? _dataSourceFactory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private DbConnection? _connection;
    private bool _disposed;

    public DbTransaction? CurrentTransaction { get; internal set; }

    public void SetActiveTransaction(DbTransaction? tx)
    {
        CurrentTransaction = tx;
    }

    public ConnectionScope(NpgsqlDataSourceFactory? dataSourceFactory = null)
    {
        _dataSourceFactory = dataSourceFactory;
    }

    /// <summary>
    /// Returns the request-scoped open DbConnection (lazily opened and thread-safe).
    /// Automatically replaces a dropped connection if no transaction is active.
    /// </summary>
    public async ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_dataSourceFactory is null)
        {
            throw new InvalidOperationException("No database connection factory configured (offline mode).");
        }

        // Fast path: connection is already open and healthy
        DbConnection? existing = _connection;
        if (existing is not null && existing.State == ConnectionState.Open)
        {
            return existing;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connection is not null)
            {
                if (_connection.State == ConnectionState.Open)
                {
                    return _connection;
                }

                // If a non-transactional connection dropped, dispose the dead object
                // and rent a fresh warm connection from the pool.
                if (CurrentTransaction is null)
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                    _connection = null;
                }
                else
                {
                    return _connection;
                }
            }

            _connection = await _dataSourceFactory
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Opens a standalone pooled DbConnection that the caller MUST dispose immediately
    /// via 'await using'. Prevents background workers from holding connections across sleeps.
    /// </summary>
    public ValueTask<DbConnection> OpenStandaloneConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_dataSourceFactory is null)
        {
            throw new InvalidOperationException("No database connection factory configured (offline mode).");
        }

        return _dataSourceFactory.OpenConnectionAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CurrentTransaction?.Dispose();
        CurrentTransaction = null;

        _connection?.Dispose();
        _connection = null;

        _connectionLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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

        _connectionLock.Dispose();
    }
}
