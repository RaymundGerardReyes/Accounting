using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Represents a state-mutating command that returns a status Result.
/// </summary>
public interface ICommand;

/// <summary>
/// Represents a state-mutating command that returns a typed value wrapped in Result.
/// </summary>
public interface ICommand<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
