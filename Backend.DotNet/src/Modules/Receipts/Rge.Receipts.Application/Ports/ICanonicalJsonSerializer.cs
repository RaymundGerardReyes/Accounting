using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

/// <summary>
/// Serializes receipt payloads into key-sorted canonical JSON and computes sha256:v2 digests.
/// </summary>
public interface ICanonicalJsonSerializer
{
    string SerializeCanonical<T>(T payload);
    AuditDigest ComputeReceiptDigest<T>(T payload);
}

