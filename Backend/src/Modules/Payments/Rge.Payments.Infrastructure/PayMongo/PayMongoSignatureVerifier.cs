using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Infrastructure.PayMongo;

/// <summary>
/// Verifies incoming PayMongo webhook requests using HMAC-SHA256, constant-time signature
/// comparison, and a 5-minute replay tolerance window.
/// </summary>
public sealed class PayMongoSignatureVerifier
{
    private readonly PayMongoOptions _options;
    private readonly IClock _clock;

    public PayMongoSignatureVerifier(IOptions<PayMongoOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options.Value;
        _clock = clock;
    }

    public bool IsValidSignature(string? signatureHeader, string rawJsonBody)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        // If test signatures are disabled in Production, reject headers that lack a live signature (li=...)
        if (!_options.AllowTestWebhookSignatures &&
            signatureHeader.Contains("li=,", StringComparison.Ordinal))
        {
            return false;
        }

        return HmacVerifier.VerifyPayMongoSignature(
            signatureHeader: signatureHeader,
            rawBody: rawJsonBody,
            webhookSecret: _options.WebhookSecret,
            nowUtc: _clock.UtcNow,
            tolerance: TimeSpan.FromMinutes(5));
    }
}

