using System.Security.Cryptography;
using System.Text;

namespace Rge.BuildingBlocks.Infrastructure.Crypto;

/// <summary>
/// Provides AES-256-GCM authenticated encryption at rest for sensitive PII,
/// vendor bank account numbers, and stored credentials.
/// Ciphertext layout: v1:{Base64(12-byte Nonce || 16-byte Tag || CiphertextBytes)}
/// </summary>
public sealed class FieldEncryptionService
{
    private const int KeySizeBytes = 32;   // 256 bits
    private const int NonceSizeBytes = 12; // 96 bits (recommended for GCM)
    private const int TagSizeBytes = 16;   // 128-bit authentication tag
    private const string VersionPrefix = "v1:";

    private readonly byte[] _masterKey;

    public FieldEncryptionService(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        if (masterKey.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"AES-256-GCM requires a 32-byte (256-bit) key; received {masterKey.Length} bytes.",
                nameof(masterKey));
        }

        _masterKey = [.. masterKey];
    }

    public string Encrypt(string plaintext, string? associatedData = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] payload = new byte[NonceSizeBytes + TagSizeBytes + plaintextBytes.Length];

        Span<byte> nonce = payload.AsSpan(0, NonceSizeBytes);
        Span<byte> tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes);
        Span<byte> ciphertext = payload.AsSpan(NonceSizeBytes + TagSizeBytes, plaintextBytes.Length);

        RandomNumberGenerator.Fill(nonce);

        byte[]? aad = associatedData is not null ? Encoding.UTF8.GetBytes(associatedData) : null;

        using var aesGcm = new AesGcm(_masterKey, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);

        return $"{VersionPrefix}{Convert.ToBase64String(payload)}";
    }

    public string Decrypt(string encryptedPayload, string? associatedData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPayload);

        if (!encryptedPayload.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unsupported or corrupted ciphertext version prefix.");
        }

        byte[] raw = Convert.FromBase64String(encryptedPayload[VersionPrefix.Length..]);
        if (raw.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Ciphertext payload is truncated.");
        }

        ReadOnlySpan<byte> nonce = raw.AsSpan(0, NonceSizeBytes);
        ReadOnlySpan<byte> tag = raw.AsSpan(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = raw.AsSpan(NonceSizeBytes + TagSizeBytes);

        byte[] plaintextBytes = new byte[ciphertext.Length];
        byte[]? aad = associatedData is not null ? Encoding.UTF8.GetBytes(associatedData) : null;

        using var aesGcm = new AesGcm(_masterKey, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}

