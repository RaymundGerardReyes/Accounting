using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Abstractions;



/// <summary>

/// Delegate representing the next step in the vertical-slice handler pipeline.

/// </summary>

public delegate Task<Result<TResponse>> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);



/// <summary>

/// Non-reflection validator contract for commands and queries.

/// </summary>

public interface IValidator<in TRequest>

{

    Result Validate(TRequest request);

}



/// <summary>

/// Marker interface for requests that require an authenticated RBAC role or department scope check.

/// </summary>

public interface IAuthorizedRequest

{

    string? RequiredRole { get; }

    string? TargetDepartmentId { get; }

}



/// <summary>

/// Marker interface for commands that must append an immutable row to audit_events upon success.

/// </summary>

public interface IAuditableCommand

{

    string AuditAction { get; }

    string EntityType { get; }

    string EntityId { get; }

    string? Reason { get; }

}



/// <summary>

/// Marker interface for read queries whose responses can be cached in HybridCache / L1 memory.

/// </summary>

public interface ICacheableQuery

{

    string CacheKey { get; }

    TimeSpan? Expiration { get; }

}
