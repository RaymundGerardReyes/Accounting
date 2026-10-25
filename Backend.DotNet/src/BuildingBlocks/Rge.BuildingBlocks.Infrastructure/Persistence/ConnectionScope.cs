using System.Data.Common;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Scoped connection holder that lazily opens a single DbConnection per HTTP request

/// and shares any active DbTransaction across repositories and the Outbox writer.

/// </summary>

public sealed class ConnectionScope : IAsyncDisposable

{

    private readonly NpgsqlDataSourceFactory _dataSourceFactory;

    private DbConnection? _connection;



    public DbTransaction? CurrentTransaction { get; internal set; }



    public ConnectionScope(NpgsqlDataSourceFactory dataSourceFactory)

    {

        ArgumentNullException.ThrowIfNull(dataSourceFactory);

        _dataSourceFactory = dataSourceFactory;

    }



    public async ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)

    {

        if (_connection is null)

        {

            _connection = await _dataSourceFactory

                .OpenConnectionAsync(cancellationToken)

                .ConfigureAwait(false);

        }



        return _connection;

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
