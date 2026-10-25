using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Composes the 6 vertical-slice behaviors (Logging, Authorization, Validation,
/// Caching, Transaction, and Audit) around a command or query handler without reflection.
/// </summary>
public sealed class RequestPipelineExecutor
{
    private readonly ICurrentActor _currentActor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IApplicationCache _cache;
    private readonly IAuditBehaviorWriter _auditWriter;

    public RequestPipelineExecutor(
        ICurrentActor currentActor,
        IUnitOfWork unitOfWork,
        IApplicationCache cache,
        IAuditBehaviorWriter auditWriter)
    {
        ArgumentNullException.ThrowIfNull(currentActor);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(auditWriter);

        _currentActor = currentActor;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _auditWriter = auditWriter;
    }

    public Task<Result<TResponse>> ExecuteCommandAsync<TCommand, TResponse>(
        TCommand command,
        RequestHandlerDelegate<TResponse> handlerDelegate,
        IEnumerable<IValidator<TCommand>>? validators = null,
        CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handlerDelegate);

        IValidator<TCommand>[] validatorArray = validators is not null ? [.. validators] : [];

        return LoggingBehavior.ExecuteAsync(
            command,
            _currentActor,
            ct1 => AuthorizationBehavior.ExecuteAsync(
                command,
                _currentActor,
                ct2 => ValidationBehavior.ExecuteAsync(
                    command,
                    validatorArray,
                    ct3 => TransactionBehavior.ExecuteAsync(
                        _unitOfWork,
                        ct4 => AuditBehavior.ExecuteAsync(
                            command,
                            _currentActor,
                            _auditWriter,
                            handlerDelegate,
                            ct4),
                        ct3),
                    ct2),
                ct1),
            onCompletedLog: null,
            cancellationToken);
    }

    public Task<Result<TResponse>> ExecuteQueryAsync<TQuery, TResponse>(
        TQuery query,
        RequestHandlerDelegate<TResponse> handlerDelegate,
        IEnumerable<IValidator<TQuery>>? validators = null,
        CancellationToken cancellationToken = default)
        where TQuery : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(handlerDelegate);

        IValidator<TQuery>[] validatorArray = validators is not null ? [.. validators] : [];

        return LoggingBehavior.ExecuteAsync(
            query,
            _currentActor,
            ct1 => AuthorizationBehavior.ExecuteAsync(
                query,
                _currentActor,
                ct2 => ValidationBehavior.ExecuteAsync(
                    query,
                    validatorArray,
                    ct3 => CachingBehavior.ExecuteAsync(
                        query,
                        _cache,
                        handlerDelegate,
                        ct3),
                    ct2),
                ct1),
            onCompletedLog: null,
            cancellationToken);
    }
}

