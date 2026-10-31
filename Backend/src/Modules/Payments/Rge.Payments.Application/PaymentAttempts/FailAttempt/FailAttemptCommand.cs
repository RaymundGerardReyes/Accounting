using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.PaymentAttempts.FailAttempt;

public sealed record FailAttemptCommand(
    string PaymentId,
    string? FailureCode = null,
    string? FailureMessage = null) : ICommand<FailAttemptResponse>;

public sealed record FailAttemptResponse(
    bool Success,
    string Status,
    string Message,
    PaymentAttemptDto Attempt);
