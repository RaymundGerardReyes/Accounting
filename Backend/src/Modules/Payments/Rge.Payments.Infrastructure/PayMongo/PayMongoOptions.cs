using System.Diagnostics.CodeAnalysis;

namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Configuration options for the PayMongo v1 REST API and webhook verification.
/// Property setters automatically sanitize surrounding quotes, trailing slashes, and whitespace.
/// </summary>
public sealed class PayMongoOptions
{
    public const string SectionName = "PayMongo";

    private string _baseUrl = "https://api.paymongo.com/v1";
    private string _secretKey = string.Empty;
    private string _publicKey = string.Empty;
    private string _webhookSecret = string.Empty;
    private string _successUrl = "http://localhost:3000/payment/return";
    private string _cancelUrl = "http://localhost:3000/checkout";

    public string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = SanitizeUrl(value);
    }

    public string SecretKey
    {
        get => _secretKey;
        set => _secretKey = SanitizeSecret(value);
    }

    public string PublicKey
    {
        get => _publicKey;
        set => _publicKey = SanitizeSecret(value);
    }

    public string WebhookSecret
    {
        get => _webhookSecret;
        set => _webhookSecret = SanitizeSecret(value);
    }

    /// <summary>
    /// Alias for WebhookSecret to support unified signing secret conventions.
    /// </summary>
    public string SigningSecret
    {
        get => WebhookSecret;
        set => WebhookSecret = value;
    }

    public string SuccessUrl
    {
        get => _successUrl;
        set => _successUrl = SanitizeUrl(value);
    }

    public string CancelUrl
    {
        get => _cancelUrl;
        set => _cancelUrl = SanitizeUrl(value);
    }

    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How long (seconds) a Checkout Session may sit with its Payment Intent still bound to an
    /// EXPIRED QR/source (pi_status=awaiting_next_action + src_status=expired) before the backend
    /// treats the attempt as abandoned, calls the Expire endpoint, and marks the attempt Expired.
    /// If PayMongo replaces the source (new src_ id) or resets the intent to awaiting_payment_method
    /// within this window, the session stays active. Set to 0 for immediate termination. Default is 5s.
    /// Env override: PayMongo__SourceExpiryGraceSeconds.
    /// </summary>
    public int SourceExpiryGraceSeconds { get; set; } = 5;
    public bool AllowTestWebhookSignatures { get; set; } = true;

    public static string SanitizeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        while (trimmed.Length >= 2 &&
               ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
                (trimmed.StartsWith('\'') && trimmed.EndsWith('\''))))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Sanitizes raw configuration string representation for URL options")]
    public static string SanitizeUrl(string? value)
    {
        string sanitized = SanitizeSecret(value);
        return sanitized.TrimEnd('/');
    }
}
