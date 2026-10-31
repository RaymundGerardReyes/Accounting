using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders.Events;

public sealed record OrderPaid(
    string OrderId,
    string OrderNumber,
    Money Amount,
    string PaymentAttemptId,
    DateTimeOffset PaidAtUtc) : DomainEvent;

