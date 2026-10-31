using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.PaymentAttempts.PollAndReconcile;

public sealed record PollAndReconcileQuery(
    string PaymentId,
    bool AutoReconcile = true) : IQuery<PaymentAttemptDto>;

