using System.Security.Cryptography;
using System.Text;

namespace Rge.BuildingBlocks.Infrastructure.Crypto;

/// <summary>
/// Performs constant-time byte and string comparisons to prevent timing side-channel attacks
/// on webhook HMAC signatures, CSRF tokens, and integration secrets.
/// </summary>
public static class ConstantTimeComparer
{
    public static bool EqualsConstantTime(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
