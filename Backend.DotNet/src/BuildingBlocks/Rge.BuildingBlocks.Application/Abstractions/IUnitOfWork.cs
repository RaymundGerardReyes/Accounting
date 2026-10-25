namespace Rge.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Unit of work abstraction coordinating atomic transactions and guaranteeing transactional hygiene.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Begins a new transaction asynchronously.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits all pending changes in the current transaction.
    /// </summary>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the active transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether a transaction is currently open and active.
    /// </summary>
    bool HasActiveTransaction { get; }
}
