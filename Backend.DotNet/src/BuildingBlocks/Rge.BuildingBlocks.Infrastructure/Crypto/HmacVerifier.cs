using System.Security.Cryptography;
using System.Text;

namespace Rge.BuildingBlocks.Infrastructure.Crypto;

/// <summary>
/// Verifies PayMongo webhook signatures using HMAC-SHA256, constant-time equality,
/// and a configurable timestamp tolerance window to block replay attacks.
/// </summary>
public static class HmacVerifier
{
    public static bool VerifyPayMongoSignature(
        string? signatureHeader,
        string rawBody,
        string webhookSecret,
        DateTimeOffset nowUtc,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(webhookSecret) ||
            rawBody is null)
        {
            return false;
        }

        string? timestampPart = null;
        string? testSig = null;
        string? liveSig = null;

        foreach (string segment in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith("t=", StringComparison.Ordinal))
            {
                timestampPart = segment[2..];
            }
            else if (segment.StartsWith("te=", StringComparison.Ordinal))
            {
                testSig = segment[3..];
            }
            else if (segment.StartsWith("li=", StringComparison.Ordinal))
            {
                liveSig = segment[3..];
            }
        }

        if (string.IsNullOrWhiteSpace(timestampPart) ||
            !long.TryParse(timestampPart, out long unixSeconds))
        {
            return false;
        }

        TimeSpan maxSkew = tolerance ?? TimeSpan.FromMinutes(5);
        DateTimeOffset signatureTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((nowUtc - signatureTime).TotalSeconds) > maxSkew.TotalSeconds)
        {
            return false;
        }

        string signedPayload = $"{timestampPart}.{rawBody}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(webhookSecret);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(signedPayload);

        byte[] computedHash = HMACSHA256.HashData(keyBytes, payloadBytes);
        string computedHex = Convert.ToHexString(computedHash);

        string? candidateSig = !string.IsNullOrWhiteSpace(liveSig) ? liveSig : testSig;
        if (string.IsNullOrWhiteSpace(candidateSig))
        {
            return false;
        }

        return ConstantTimeComparer.EqualsConstantTime(computedHex, candidateSig.ToUpperInvariant());
    }
}
