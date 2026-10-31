using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Reconciliation.ReconcileOne;

public sealed record ReconcileOneCommand(string PaymentId) : ICommand<ReconcileOneResponse>;

public sealed record ReconcileOneResponse(
    string Status,
    string PaymentId,
    string? OrderId,
    PaymentAttemptDto? Attempt,
    string Message);
