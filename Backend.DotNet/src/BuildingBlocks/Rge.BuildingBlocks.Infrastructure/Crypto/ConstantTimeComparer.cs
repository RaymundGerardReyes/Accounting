using System.Security.Cryptography;

using System.Text;



namespace Rge.BuildingBlocks.Infrastructure.Crypto;



/// <summary>

/// Performs zero-allocation, constant-time byte and string comparisons to prevent

/// timing and length side-channel attacks on webhook HMAC signatures, CSRF tokens, and secrets.

/// </summary>

public static class ConstantTimeComparer

{

    public static bool EqualsConstantTime(string? left, string? right)

    {

        if (left is null || right is null)

        {

            return false;

        }



        // Hash both inputs into stack-allocated 32-byte SHA-256 buffers so that:

        // 1. Zero heap allocations occur regardless of input length.

        // 2. Comparison always runs in fixed time over 32 bytes without leaking secret length.

        Span<byte> leftHash = stackalloc byte[32];

        Span<byte> rightHash = stackalloc byte[32];



        HashUtf8String(left, leftHash);

        HashUtf8String(right, rightHash);



        bool hashesMatch = CryptographicOperations.FixedTimeEquals(leftHash, rightHash);

        return hashesMatch && left.Length == right.Length;

    }



    public static bool EqualsConstantTimeBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>

        CryptographicOperations.FixedTimeEquals(left, right);



    private static void HashUtf8String(ReadOnlySpan<char> value, Span<byte> destination)

    {

        int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);

        if (maxByteCount <= 512)

        {

            Span<byte> utf8Buffer = stackalloc byte[maxByteCount];

            int written = Encoding.UTF8.GetBytes(value, utf8Buffer);

            SHA256.HashData(utf8Buffer[..written], destination);

        }

        else

        {

            byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxByteCount);

            try

            {

                int written = Encoding.UTF8.GetBytes(value, rented);

                SHA256.HashData(rented.AsSpan(0, written), destination);

            }

            finally

            {

                System.Buffers.ArrayPool<byte>.Shared.Return(rented, clearArray: true);

            }

        }

    }

}
