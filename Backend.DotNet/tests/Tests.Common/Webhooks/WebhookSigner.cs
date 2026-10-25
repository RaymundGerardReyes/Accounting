using System.Security.Cryptography;
using System.Text;

namespace Rge.Tests.Common.Webhooks;

/// <summary>
/// Generates deterministic valid, expired, and tampered Paymongo-Signature headers for tests.
/// Format: t={timestamp},te={test_signature},li={live_signature}
/// </summary>
public static class WebhookSigner
{
    public static string CreateSignatureHeader(
        string rawBody,
        string secret,
        long timestampUnix,
        bool isLive = false,
        bool tamper = false)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(secret);

        string signedPayload = $"{timestampUnix}.{rawBody}";
        if (tamper)
        {
            signedPayload += "_tampered";
        }

        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(signedPayload);
        byte[] hash = HMACSHA256.HashData(secretBytes, payloadBytes);
        string signature = Convert.ToHexStringLower(hash);

        return isLive
            ? $"t={timestampUnix},li={signature}"
            : $"t={timestampUnix},te={signature}";
    }
}

