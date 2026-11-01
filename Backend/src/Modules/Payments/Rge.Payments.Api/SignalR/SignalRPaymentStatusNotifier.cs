using Microsoft.AspNetCore.SignalR;
using Rge.Payments.Application.Ports;
using Rge.Payments.Api.Hubs;

namespace Rge.Payments.Api.SignalR;

/// <summary>
/// Implements <see cref="IPaymentStatusNotifier"/> using SignalR.
/// Lives in the Api layer so that the Application layer does not depend on
/// Microsoft.AspNetCore.SignalR directly (port/adapter pattern).
/// </summary>
public sealed class SignalRPaymentStatusNotifier : IPaymentStatusNotifier
{
    private readonly IHubContext<PaymentHub> _hubContext;

    public SignalRPaymentStatusNotifier(IHubContext<PaymentHub> hubContext)
    {
        ArgumentNullException.ThrowIfNull(hubContext);
        _hubContext = hubContext;
    }

    public Task NotifyPaymentStatusChangedAsync(
        string transactionId,
        string newStatus,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        string groupName = PaymentHub.GroupName(transactionId);

        return _hubContext.Clients
            .Group(groupName)
            .SendAsync(
                "PaymentStatusChanged",
                new
                {
                    transactionId,
                    status = newStatus,
                    occurredAt = occurredAtUtc.ToString("O"),
                },
                cancellationToken);
    }
}

