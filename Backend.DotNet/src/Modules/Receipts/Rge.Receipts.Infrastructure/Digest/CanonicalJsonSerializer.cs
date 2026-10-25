using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Digest;

/// <summary>
/// Implements ICanonicalJsonSerializer using Rge.BuildingBlocks.Infrastructure.Crypto primitives.
/// </summary>
public sealed class CanonicalJsonSerializer : ICanonicalJsonSerializer
{
    public string SerializeCanonical<T>(T payload) =>
        CanonicalJson.SerializeDeterministic(payload);

    public AuditDigest ComputeReceiptDigest<T>(T payload)
    {
        string canonicalJson = CanonicalJson.SerializeDeterministic(payload);
        string digestString = HashService.ComputeBirReceiptDigestV2(canonicalJson);
        return new AuditDigest(digestString);
    }
}
