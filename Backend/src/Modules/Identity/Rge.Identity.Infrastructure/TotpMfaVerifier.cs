using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Rge.Identity.Infrastructure;

/// <summary>
/// Implements RFC 6238 Time-Based One-Time Password (TOTP) verification
/// for Multi-Factor Authentication (MFA) without external NuGet dependencies.
/// </summary>
public static class TotpMfaVerifier
{
    private const int TimeStepSeconds = 30;
    private const int CodeDigits = 6;
    private const int DigitModulo = 1_000_000;

    /// <summary>
    /// Verifies a 6-digit authenticator app code against the user's secret key,
    /// allowing a +/- 1 time-step window (30 seconds) for clock drift.
    /// </summary>
    public static bool VerifyTotpCode(
        ReadOnlySpan<byte> secretKey,
        string? candidateCode,
        DateTimeOffset nowUtc,
        int allowedDriftSteps = 1)
    {
        if (secretKey.IsEmpty ||
            string.IsNullOrWhiteSpace(candidateCode) ||
            candidateCode.Length != CodeDigits ||
            !int.TryParse(candidateCode, out int parsedCandidate))
        {
            return false;
        }

        long currentCounter = nowUtc.ToUnixTimeSeconds() / TimeStepSeconds;

        for (int offset = -allowedDriftSteps; offset <= allowedDriftSteps; offset++)
        {
            int expectedCode = ComputeTotpForCounter(secretKey, currentCounter + offset);
            if (CryptographicOperations.FixedTimeEquals(
                    BitConverter.GetBytes(expectedCode),
                    BitConverter.GetBytes(parsedCandidate)))
            {
                return true;
            }
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 6238 specifies HMAC-SHA1 as the required default algorithm for interoperability with authenticator apps (Google Authenticator, Microsoft Authenticator)")]
    private static int ComputeTotpForCounter(ReadOnlySpan<byte> secretKey, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hmac = stackalloc byte[20]; // HMAC-SHA1 per RFC 6238
        HMACSHA1.HashData(secretKey, counterBytes, hmac);

        int offset = hmac[^1] & 0x0F;
        int binaryCode =
            ((hmac[offset] & 0x7F) << 24) |
            ((hmac[offset + 1] & 0xFF) << 16) |
            ((hmac[offset + 2] & 0xFF) << 8) |
            (hmac[offset + 3] & 0xFF);

        return binaryCode % DigitModulo;
    }
}

