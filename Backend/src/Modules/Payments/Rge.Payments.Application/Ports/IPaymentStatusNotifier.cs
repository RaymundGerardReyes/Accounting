namespace Rge.Payments.Application.Ports;

/// <summary>
/// Port interface that the Application layer uses to push real-time payment
/// status changes to connected clients.  The actual SignalR implementation
/// lives in Rge.Payments.Api so the Application layer stays framework-free.
/// </summary>
public interface IPaymentStatusNotifier
{
    /// <summary>
    /// Broadcasts a payment status change to all clients subscribed to the
    /// given <paramref name="transactionId"/> group.
    /// </summary>
    Task NotifyPaymentStatusChangedAsync(
        string transactionId,
        string newStatus,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}

