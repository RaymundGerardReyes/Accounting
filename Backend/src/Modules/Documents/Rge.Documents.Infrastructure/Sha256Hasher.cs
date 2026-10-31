using Rge.BuildingBlocks.Infrastructure.Crypto;

namespace Rge.Documents.Infrastructure;

public static class Sha256Hasher
{
    public static string ComputeHex(ReadOnlySpan<byte> bytes) =>
        HashService.ComputeSha256Hex(bytes);
}

