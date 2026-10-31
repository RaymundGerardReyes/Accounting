namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Configuration options for the PayMongo v1 REST API and webhook verification.
/// Secrets are injected from environment variables or vault at runtime.
/// </summary>
public sealed class PayMongoOptions
{
    public const string SectionName = "PayMongo";

    public string BaseUrl { get; set; } = "https://api.paymongo.com/v1/";
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = "http://localhost:5173/payments/success";
    public string CancelUrl { get; set; } = "http://localhost:5173/payments/cancel";
    public int RequestTimeoutSeconds { get; set; } = 10;
    public bool AllowTestWebhookSignatures { get; set; } = true;
}

