using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders.Events;

public sealed record OrderRefunded(
    string OrderId,
    string OrderNumber,
    Money Amount,
    string Reason,
    DateTimeOffset RefundedAtUtc) : DomainEvent;

