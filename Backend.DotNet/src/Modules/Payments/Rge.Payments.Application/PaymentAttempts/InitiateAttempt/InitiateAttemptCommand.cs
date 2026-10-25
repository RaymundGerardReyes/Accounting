using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.PaymentAttempts.InitiateAttempt;

public sealed record InitiateAttemptCommand(
    string OrderId,
    string? PaymentMethod = null,
    string? IdempotencyKey = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    string? SuccessUrl = null,
    string? CancelUrl = null) : ICommand<PaymentAttemptDto>;

