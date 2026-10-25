using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Domain.Errors;

public static class FinanceErrors
{
    public static Error NotFound(string entityName, string id) =>
        Error.NotFound("Finance.NotFound", $"{entityName} with id '{id}' was not found.");

    public static Error Unauthorized(string message = "User is not authenticated.") =>
        Error.Unauthorized("Finance.Unauthorized", message);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        Error.Forbidden("Finance.Forbidden", message);

    public static Error SodViolation(string ruleCode, string message) =>
        Error.Forbidden($"Finance.SoD.{ruleCode}", message);

    public static Error Validation(string code, string message) =>
        Error.Validation($"Finance.{code}", message);

    public static Error Conflict(string code, string message) =>
        Error.Conflict($"Finance.{code}", message);

    public static Error PolicyViolation(string ruleCode, string message) =>
        Error.Validation($"Finance.Policy.{ruleCode}", message);

    public static Error InvalidTransition(string machine, string from, string to) =>
        Error.Validation($"Finance.InvalidTransition.{machine}", $"Cannot transition {machine} from '{from}' to '{to}'.");
}
