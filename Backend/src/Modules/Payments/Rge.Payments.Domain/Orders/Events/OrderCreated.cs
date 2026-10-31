using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders.Events;

public sealed record OrderCreated(
    string OrderId,
    string OrderNumber,
    Money Amount,
    string Category,
    DateTimeOffset CreatedAtUtc) : DomainEvent;

