using System.Globalization;

using System.Security.Cryptography;

using System.Text;

using Rge.BuildingBlocks.Infrastructure.Crypto;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Infrastructure;



/// <summary>

/// Issues and verifies session-bound HMAC-SHA256 anti-forgery (CSRF) tokens.

/// Uses ConstantTimeComparer to prevent timing side-channel attacks.

/// </summary>

public sealed class CsrfTokenService : ICsrfTokenService

{

    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(8);

    private readonly byte[] _signingKey;



    public CsrfTokenService(byte[] signingKey)

    {

        ArgumentNullException.ThrowIfNull(signingKey);

        if (signingKey.Length < 32)

        {

            throw new ArgumentException("CSRF HMAC signing key must be at least 256 bits (32 bytes).", nameof(signingKey));

        }



        _signingKey = [.. signingKey];

    }



    public string GenerateToken(string sessionTokenHash, DateTimeOffset issuedAtUtc)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionTokenHash);



        long issuedUnixSeconds = issuedAtUtc.ToUnixTimeSeconds();

        string timestampHex = issuedUnixSeconds.ToString("x16", CultureInfo.InvariantCulture);

        string signatureHex = ComputeHmacHex(sessionTokenHash, timestampHex);



        return $"{timestampHex}.{signatureHex}";

    }



    public bool ValidateToken(string sessionTokenHash, string? candidateToken, DateTimeOffset nowUtc)

    {

        if (string.IsNullOrWhiteSpace(sessionTokenHash) || string.IsNullOrWhiteSpace(candidateToken))

        {

            return false;

        }



        int dotIndex = candidateToken.IndexOf('.', StringComparison.Ordinal);

        if (dotIndex <= 0 || dotIndex == candidateToken.Length - 1)

        {

            return false;

        }



        string timestampHex = candidateToken[..dotIndex];

        string providedSignatureHex = candidateToken[(dotIndex + 1)..];



        if (!long.TryParse(timestampHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long issuedUnixSeconds))

        {

            return false;

        }



        DateTimeOffset issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(issuedUnixSeconds);

        TimeSpan age = nowUtc - issuedAtUtc;

        if (age < TimeSpan.FromMinutes(-5) || age > DefaultTokenLifetime)

        {

            return false;

        }



        string expectedSignatureHex = ComputeHmacHex(sessionTokenHash, timestampHex);

        return ConstantTimeComparer.EqualsConstantTime(expectedSignatureHex, providedSignatureHex);

    }



    private string ComputeHmacHex(string sessionTokenHash, string timestampHex)

    {

        string payload = $"{sessionTokenHash}:{timestampHex}";

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

        Span<byte> hmacBytes = stackalloc byte[32];

        HMACSHA256.HashData(_signingKey, payloadBytes, hmacBytes);

        return Convert.ToHexStringLower(hmacBytes);

    }

}
