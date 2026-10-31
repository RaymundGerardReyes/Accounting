using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Domain.Errors;

/// <summary>
/// Domain error catalog for the Payments module.
/// </summary>
public static class PaymentErrors
{
    public static Error TerminalStateImmutable(string attemptId, PaymentAttemptStatus currentStatus) =>
        Error.Conflict(
            "Payments.TerminalStateImmutable",
            $"Payment attempt '{attemptId}' is already in terminal state '{currentStatus}' and cannot be modified.");

    public static Error InvalidStateTransition(PaymentAttemptStatus from, PaymentAttemptStatus to) =>
        Error.Validation(
            "Payments.InvalidStateTransition",
            $"Cannot transition payment attempt from '{from}' to '{to}'.");

    public static Error OrderAlreadyPaid(string orderNumber) =>
        Error.Conflict(
            "Payments.OrderAlreadyPaid",
            $"Order '{orderNumber}' is already marked as paid.");
}

