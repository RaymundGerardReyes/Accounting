using Microsoft.AspNetCore.SignalR;

namespace Rge.Payments.Api.Hubs;

/// <summary>
/// SignalR hub for real-time payment status notifications.
/// Clients join a group named "payment:{transactionId}" to receive updates
/// for a specific payment attempt without polling.
/// </summary>
public sealed class PaymentHub : Hub
{
    /// <summary>
    /// Joins the caller to the SignalR group for a specific payment attempt.
    /// Call this immediately after connecting and receiving the transactionId.
    /// </summary>
    public Task JoinPaymentGroupAsync(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(transactionId));
    }

    /// <summary>
    /// Leaves the SignalR group for a specific payment attempt.
    /// Call this when the payment result page is unmounted.
    /// </summary>
    public Task LeavePaymentGroupAsync(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(transactionId));
    }

    /// <summary>Returns the canonical group name for a payment transaction.</summary>
    public static string GroupName(string transactionId) => $"payment:{transactionId}";
}

