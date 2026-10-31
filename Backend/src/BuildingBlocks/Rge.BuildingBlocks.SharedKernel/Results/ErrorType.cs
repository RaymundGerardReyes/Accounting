namespace Rge.BuildingBlocks.SharedKernel.Results;

/// <summary>
/// Classifies domain errors for deterministic mapping to HTTP ProblemDetails (RFC 9457).
/// </summary>
public enum ErrorType
{
    None = 0,
    Validation = 400,
    Unauthorized = 401,
    Forbidden = 403,
    SodViolation = 4031,
    NotFound = 404,
    Conflict = 409,
    PolicyViolation = 422,
    Failure = 500
}

