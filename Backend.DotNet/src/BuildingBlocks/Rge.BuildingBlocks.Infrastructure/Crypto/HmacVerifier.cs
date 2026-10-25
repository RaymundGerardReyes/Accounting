using System.Buffers;

using System.Security.Cryptography;

using System.Text;



namespace Rge.BuildingBlocks.Infrastructure.Crypto;



/// <summary>

/// Verifies PayMongo webhook signatures using zero-allocation HMAC-SHA256,

/// constant-time byte comparison, and a strict timestamp replay window.

/// </summary>

public static class HmacVerifier

{

    public static bool VerifyPayMongoSignature(

        string? signatureHeader,

        string rawBody,

        string webhookSecret,

        DateTimeOffset nowUtc,

        TimeSpan? tolerance = null,

        bool requireLiveSignature = false)

    {

        if (string.IsNullOrWhiteSpace(signatureHeader) ||

            string.IsNullOrWhiteSpace(webhookSecret) ||

            rawBody is null)

        {

            return false;

        }



        ReadOnlySpan<char> headerSpan = signatureHeader.AsSpan();

        ReadOnlySpan<char> timestampPart = default;

        ReadOnlySpan<char> testSig = default;

        ReadOnlySpan<char> liveSig = default;



        foreach (Range segmentRange in headerSpan.Split(','))

        {

            ReadOnlySpan<char> segment = headerSpan[segmentRange].Trim();

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



        if (timestampPart.IsEmpty || !long.TryParse(timestampPart, out long unixSeconds))

        {

            return false;

        }



        TimeSpan maxSkew = tolerance ?? TimeSpan.FromMinutes(5);

        DateTimeOffset signatureTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        if (Math.Abs((nowUtc - signatureTime).TotalSeconds) > maxSkew.TotalSeconds)

        {

            return false;

        }



        ReadOnlySpan<char> candidateSigHex = requireLiveSignature

            ? liveSig

            : (!liveSig.IsEmpty ? liveSig : testSig);



        if (candidateSigHex.Length != 64) // SHA-256 hex is always 64 chars (32 bytes)

        {

            return false;

        }



        Span<byte> candidateBytes = stackalloc byte[32];

        if (Convert.FromHexString(candidateSigHex, candidateBytes, out _, out _) != OperationStatus.Done)

        {

            return false;

        }



        // Compute HMAC-SHA256 over "{timestamp}.{rawBody}" using pooled buffer to avoid LOH allocations

        int keyByteCount = Encoding.UTF8.GetMaxByteCount(webhookSecret.Length);

        byte[] keyArray = ArrayPool<byte>.Shared.Rent(keyByteCount);



        int payloadMaxBytes = Encoding.UTF8.GetMaxByteCount(timestampPart.Length + 1 + rawBody.Length);

        byte[] payloadArray = ArrayPool<byte>.Shared.Rent(payloadMaxBytes);



        try

        {

            int keyWritten = Encoding.UTF8.GetBytes(webhookSecret.AsSpan(), keyArray);

            int pos = Encoding.UTF8.GetBytes(timestampPart, payloadArray);

            payloadArray[pos++] = (byte)'.';

            pos += Encoding.UTF8.GetBytes(rawBody.AsSpan(), payloadArray.AsSpan(pos));



            Span<byte> computedHash = stackalloc byte[32];

            HMACSHA256.HashData(

                keyArray.AsSpan(0, keyWritten),

                payloadArray.AsSpan(0, pos),

                computedHash);



            return CryptographicOperations.FixedTimeEquals(computedHash, candidateBytes);

        }

        finally

        {

            ArrayPool<byte>.Shared.Return(keyArray, clearArray: true);

            ArrayPool<byte>.Shared.Return(payloadArray, clearArray: false);

        }

    }

}
