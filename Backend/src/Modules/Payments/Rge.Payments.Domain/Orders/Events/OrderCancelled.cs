using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders.Events;

public sealed record OrderCancelled(
    string OrderId,
    string OrderNumber,
    string Reason,
    DateTimeOffset CancelledAtUtc) : DomainEvent;

