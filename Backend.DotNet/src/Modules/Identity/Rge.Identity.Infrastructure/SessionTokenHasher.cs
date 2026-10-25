using System.Security.Cryptography;
using System.Text;

namespace Rge.Identity.Infrastructure;

/// <summary>
/// Generates 256-bit cryptographic session tokens and computes SHA-256 storage hashes
/// so raw session tokens are never persisted in plaintext in the database.
/// </summary>
public static class SessionTokenHasher
{
    private const int TokenByteLength = 32; // 256 bits

    /// <summary>
    /// Generates a URL-safe Base64 256-bit session token and its SHA-256 hex digest.
    /// Send <c>RawToken</c> in an HttpOnly, Secure, SameSite=Strict cookie; store <c>TokenHash</c> in DB.
    /// </summary>
    public static (string RawToken, string TokenHash) CreateSessionToken()
    {
        Span<byte> randomBytes = stackalloc byte[TokenByteLength];
        RandomNumberGenerator.Fill(randomBytes);

        string rawToken = Base64UrlEncode(randomBytes);
        string tokenHash = HashToken(rawToken);

        return (rawToken, tokenHash);
    }

    public static string HashToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        byte[] inputBytes = Encoding.UTF8.GetBytes(rawToken);
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(inputBytes, hashBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

