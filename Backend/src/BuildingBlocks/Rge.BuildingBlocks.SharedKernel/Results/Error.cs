namespace Rge.BuildingBlocks.SharedKernel.Results;

/// <summary>
/// Represents a structured domain or application error without throwing exceptions.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Validation(string code, string message) =>
        new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) =>
        new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) =>
        new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) =>
        new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) =>
        new(code, message, ErrorType.Forbidden);

    public static Error SodViolation(string code, string message) =>
        new(code, message, ErrorType.SodViolation);

    public static Error PolicyViolation(string code, string message) =>
        new(code, message, ErrorType.PolicyViolation);
}

