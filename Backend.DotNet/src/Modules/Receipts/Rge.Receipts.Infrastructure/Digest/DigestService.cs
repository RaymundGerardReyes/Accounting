using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Digest;

/// <summary>
/// Verifies tamper-evident sha256:v2 digests on BIR Official Receipts.
/// </summary>
public static class DigestService
{
    public static bool VerifyDigest<T>(T canonicalPayload, AuditDigest expectedDigest)
    {
        ArgumentNullException.ThrowIfNull(expectedDigest);

        string canonicalJson = CanonicalJson.SerializeDeterministic(canonicalPayload);
        string computed = HashService.ComputeBirReceiptDigestV2(canonicalJson);
        return ConstantTimeComparer.EqualsConstantTime(computed, expectedDigest.Value);
    }
}
