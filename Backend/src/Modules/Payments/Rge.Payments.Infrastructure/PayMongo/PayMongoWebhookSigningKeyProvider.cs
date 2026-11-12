using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Dynamically provides the PayMongo webhook signing key with IOptionsMonitor support,
/// multi-level configuration/environment fallback, and automatic quote/whitespace/slash sanitization.
/// </summary>
public sealed class PayMongoWebhookSigningKeyProvider : IWebhookSigningKeyProvider
{
    private readonly IOptionsMonitor<PayMongoOptions>? _optionsMonitor;
    private readonly IOptions<PayMongoOptions>? _options;
    private readonly IConfiguration? _configuration;

    private static readonly string[] ConfigurationFallbackKeys =
    [
        "PayMongo:WebhookSecret",
        "PayMongo:SigningSecret",
        "Secrets:PayMongoWebhookSecret",
        "Secrets__PayMongoWebhookSecret",
        "PAYMONGO_WEBHOOK_SECRET",
        "PAYMONGO_WEBHOOK_SECRET_KEY",
        "PAYMONGO_SIGNING_SECRET"
    ];

    private static readonly string[] EnvironmentFallbackKeys =
    [
        "PAYMONGO_WEBHOOK_SECRET",
        "PAYMONGO_WEBHOOK_SECRET_KEY",
        "PAYMONGO_SIGNING_SECRET"
    ];

    public PayMongoWebhookSigningKeyProvider(
        IOptionsMonitor<PayMongoOptions> optionsMonitor,
        IConfiguration? configuration = null,
        IOptions<PayMongoOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _optionsMonitor = optionsMonitor;
        _configuration = configuration;
        _options = options;
    }

    public PayMongoWebhookSigningKeyProvider(
        IOptions<PayMongoOptions> options,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _configuration = configuration;
    }

    public string GetWebhookSigningKey()
    {
        // 1. Primary: Dynamic options monitor
        string? candidate = _optionsMonitor?.CurrentValue?.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return Sanitize(candidate);
        }

        // 2. Fallback: IOptions snapshot (for test setups)
        candidate = _options?.Value?.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return Sanitize(candidate);
        }

        // 3. Fallback: IConfiguration keys
        if (_configuration is not null)
        {
            foreach (string key in ConfigurationFallbackKeys)
            {
                string? val = _configuration[key];
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return Sanitize(val);
                }
            }
        }

        // 4. Fallback: Environment variables
        foreach (string envKey in EnvironmentFallbackKeys)
        {
            string? val = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(val))
            {
                return Sanitize(val);
            }
        }

        return string.Empty;
    }

    private static string Sanitize(string? raw)
    {
        return PayMongoOptions.SanitizeSecret(raw);
    }
}
