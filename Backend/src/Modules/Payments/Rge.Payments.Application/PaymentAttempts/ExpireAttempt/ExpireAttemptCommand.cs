using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.PaymentAttempts.ExpireAttempt;

public sealed record ExpireAttemptCommand(
    string PaymentId,
    string? Reason = null) : ICommand<ExpireAttemptResponse>;

public sealed record ExpireAttemptResponse(
    bool Success,
    string Status,
    string Message,
    PaymentAttemptDto Attempt);
