namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Represents the lifecycle states of a PayMongo checkout session / payment attempt.
/// </summary>
public enum PaymentAttemptStatus
{
    Pending = 0,
    RequiresAction = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6,
    Unknown = 7,
    DuplicatePaidReview = 8
}

