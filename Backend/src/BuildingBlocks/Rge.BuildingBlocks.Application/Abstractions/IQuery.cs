using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Represents a read-only query returning <typeparamref name="TResponse"/>.
/// </summary>
public interface IQuery<TResponse>;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
