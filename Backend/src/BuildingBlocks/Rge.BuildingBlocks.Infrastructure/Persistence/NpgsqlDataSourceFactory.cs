using System.Data.Common;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Central factory over DbDataSource (implemented by NpgsqlDataSource)

/// ensuring a single pooled data source with prepared statement support is shared across modules.

/// </summary>

public sealed class NpgsqlDataSourceFactory : IAsyncDisposable, IDisposable

{

    private readonly DbDataSource _dataSource;



    public NpgsqlDataSourceFactory(DbDataSource dataSource)

    {

        ArgumentNullException.ThrowIfNull(dataSource);

        _dataSource = dataSource;

    }



    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>

        _dataSource.OpenConnectionAsync(cancellationToken);



    public DbCommand CreateCommand(string? commandText = null) =>

        _dataSource.CreateCommand(commandText);



    public void Dispose() => _dataSource.Dispose();



    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

}
