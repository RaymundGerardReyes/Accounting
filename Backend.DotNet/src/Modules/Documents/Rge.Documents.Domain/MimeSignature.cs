using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Documents.Domain;

public sealed class MimeSignature : ValueObject
{
    public string MimeType { get; }
    public string Extension { get; }

    public MimeSignature(string mimeType, string extension)
    {
        MimeType = Guard.AgainstNullOrWhiteSpace(mimeType).ToLowerInvariant();
        Extension = Guard.AgainstNullOrWhiteSpace(extension).ToLowerInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MimeType;
        yield return Extension;
    }
}

