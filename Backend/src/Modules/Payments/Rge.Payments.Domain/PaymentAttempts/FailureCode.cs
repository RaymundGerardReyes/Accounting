namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Canonical failure codes for rejected, declined, or timed-out payment attempts.
/// </summary>
public static class FailureCode
{
    public const string CardDeclined = "CARD_DECLINED";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string SessionCancelled = "SESSION_CANCELLED";
    public const string GatewayError = "GATEWAY_ERROR";
    public const string AuthenticationFailed = "AUTHENTICATION_FAILED";
    public const string NetworkError = "NETWORK_ERROR";
    public const string DuplicateTransaction = "DUPLICATE_TRANSACTION";
}

