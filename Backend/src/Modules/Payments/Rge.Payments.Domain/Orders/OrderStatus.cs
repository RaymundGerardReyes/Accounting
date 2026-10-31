namespace Rge.Payments.Domain.Orders;

/// <summary>
/// Lifecycle states for an Order aggregate.
/// </summary>
public static class OrderStatus
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";

    public static bool IsValid(string status) =>
        status is Pending or Paid or Cancelled or Refunded;

    public static bool IsTerminal(string status) =>
        status is Paid or Cancelled or Refunded;
}

