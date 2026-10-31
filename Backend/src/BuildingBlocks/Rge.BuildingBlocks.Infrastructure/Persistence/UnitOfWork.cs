using System.Data;

using System.Data.Common;

using Rge.BuildingBlocks.Application.Abstractions;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Coordinates atomic database transactions over the request-scoped ConnectionScope.

/// </summary>

public sealed class UnitOfWork : IUnitOfWork

{

    private readonly ConnectionScope _connectionScope;



    public UnitOfWork(ConnectionScope connectionScope)

    {

        ArgumentNullException.ThrowIfNull(connectionScope);

        _connectionScope = connectionScope;

    }



    public bool HasActiveTransaction => _connectionScope.CurrentTransaction is not null;



    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)

    {

        if (HasActiveTransaction)

        {

            return;

        }



        DbConnection connection = await _connectionScope

            .GetOpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        _connectionScope.CurrentTransaction = await connection

            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)

            .ConfigureAwait(false);

    }



    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)

    {

        if (_connectionScope.CurrentTransaction is null)

        {

            return 0;

        }



        try

        {

            await _connectionScope.CurrentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return 1;

        }

        finally

        {

            await _connectionScope.CurrentTransaction.DisposeAsync().ConfigureAwait(false);

            _connectionScope.CurrentTransaction = null;

        }

    }



    public async Task RollbackAsync(CancellationToken cancellationToken = default)

    {

        if (_connectionScope.CurrentTransaction is null)

        {

            return;

        }



        try

        {

            await _connectionScope.CurrentTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

        }

        finally

        {

            await _connectionScope.CurrentTransaction.DisposeAsync().ConfigureAwait(false);

            _connectionScope.CurrentTransaction = null;

        }

    }

}
