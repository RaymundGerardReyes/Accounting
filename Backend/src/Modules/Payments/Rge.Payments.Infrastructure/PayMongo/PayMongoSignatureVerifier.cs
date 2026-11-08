using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Verifies incoming PayMongo webhook requests using HMAC-SHA256, constant-time signature
/// comparison, and a 5-minute replay tolerance window.
/// </summary>
public sealed class PayMongoSignatureVerifier
{
    private readonly PayMongoOptions _options;
    private readonly IOptionsMonitor<PayMongoOptions>? _optionsMonitor;
    private readonly IClock _clock;
    private readonly IWebhookSigningKeyProvider? _keyProvider;

    public PayMongoSignatureVerifier(
        IOptionsMonitor<PayMongoOptions> optionsMonitor,
        IClock clock,
        IWebhookSigningKeyProvider? keyProvider = null,
        IOptions<PayMongoOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(clock);

        _optionsMonitor = optionsMonitor;
        _options = options?.Value ?? optionsMonitor.CurrentValue;
        _clock = clock;
        _keyProvider = keyProvider;
    }

    public PayMongoSignatureVerifier(
        IOptions<PayMongoOptions> options,
        IClock clock,
        IWebhookSigningKeyProvider? keyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options.Value;
        _clock = clock;
        _keyProvider = keyProvider;
    }

    public bool IsValidSignature(string? signatureHeader, string rawJsonBody)
    {
        string webhookSecret = _keyProvider?.GetWebhookSigningKey() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            webhookSecret = _optionsMonitor?.CurrentValue.WebhookSecret
                ?? _options.WebhookSecret;
        }

        bool allowTestSignatures = _optionsMonitor?.CurrentValue.AllowTestWebhookSignatures
            ?? _options.AllowTestWebhookSignatures;

        if (string.IsNullOrWhiteSpace(webhookSecret) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        // If test signatures are disabled in Production, reject headers that lack a live signature (li=...)
        if (!allowTestSignatures &&
            signatureHeader.Contains("li=,", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return HmacVerifier.VerifyPayMongoSignature(
                signatureHeader: signatureHeader,
                rawBody: rawJsonBody,
                webhookSecret: webhookSecret,
                nowUtc: _clock.UtcNow,
                tolerance: TimeSpan.FromMinutes(5));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
