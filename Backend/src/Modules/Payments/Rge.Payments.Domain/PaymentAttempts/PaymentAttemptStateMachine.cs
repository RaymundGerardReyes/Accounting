using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Domain.Errors;

namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Enforces valid state transitions for PaymentAttempt and guarantees terminal state immutability.
/// </summary>
public static class PaymentAttemptStateMachine
{
    public static bool IsTerminal(PaymentAttemptStatus status) =>
        status is PaymentAttemptStatus.Succeeded
            or PaymentAttemptStatus.Failed
            or PaymentAttemptStatus.Cancelled
            or PaymentAttemptStatus.Expired;

    public static Result<PaymentAttemptStatus> Transition(
        string attemptId,
        PaymentAttemptStatus current,
        PaymentAttemptStatus target)
    {
        if (current == target)
        {
            return Result.Success(current);
        }

        if (IsTerminal(current))
        {
            // Special case: if a second payment succeeds on an already-terminated attempt, flag for review
            if (target == PaymentAttemptStatus.DuplicatePaidReview)
            {
                return Result.Success(PaymentAttemptStatus.DuplicatePaidReview);
            }

            return Result.Failure<PaymentAttemptStatus>(
                PaymentErrors.TerminalStateImmutable(attemptId, current));
        }

        bool isValid = (current, target) switch
        {
            // From Pending
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.RequiresAction) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Processing) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Succeeded) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Failed) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Expired) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Cancelled) => true,
            (PaymentAttemptStatus.Pending, PaymentAttemptStatus.Unknown) => true,

            // From RequiresAction (3DS / QRPh / Redirect)
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Processing) => true,
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Succeeded) => true,
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Failed) => true,
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Expired) => true,
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Cancelled) => true,
            (PaymentAttemptStatus.RequiresAction, PaymentAttemptStatus.Unknown) => true,

            // From Processing
            (PaymentAttemptStatus.Processing, PaymentAttemptStatus.Succeeded) => true,
            (PaymentAttemptStatus.Processing, PaymentAttemptStatus.Failed) => true,
            (PaymentAttemptStatus.Processing, PaymentAttemptStatus.Unknown) => true,

            // From Unknown (Reconciliation recovery)
            (PaymentAttemptStatus.Unknown, PaymentAttemptStatus.Succeeded) => true,
            (PaymentAttemptStatus.Unknown, PaymentAttemptStatus.Failed) => true,
            (PaymentAttemptStatus.Unknown, PaymentAttemptStatus.Expired) => true,

            _ => false
        };

        return isValid
            ? Result.Success(target)
            : Result.Failure<PaymentAttemptStatus>(PaymentErrors.InvalidStateTransition(current, target));
    }
}

