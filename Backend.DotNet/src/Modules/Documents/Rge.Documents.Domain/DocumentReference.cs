using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Documents.Domain;

public sealed class DocumentReference : AggregateRoot<string>
{
    public string OriginalFilename { get; }
    public string StoredFilename { get; }
    public string MimeType { get; }
    public long SizeBytes { get; }
    public string Sha256Hash { get; }
    public string UploadedByActorId { get; }
    public DateTimeOffset UploadedAtUtc { get; }

    public DocumentReference(
        string id,
        string originalFilename,
        string storedFilename,
        string mimeType,
        long sizeBytes,
        string sha256Hash,
        string uploadedByActorId,
        DateTimeOffset uploadedAtUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        OriginalFilename = Guard.AgainstNullOrWhiteSpace(originalFilename);
        StoredFilename = Guard.AgainstNullOrWhiteSpace(storedFilename);
        MimeType = Guard.AgainstNullOrWhiteSpace(mimeType);
        SizeBytes = Guard.AgainstNonPositive(sizeBytes, nameof(sizeBytes));
        Sha256Hash = Guard.AgainstNullOrWhiteSpace(sha256Hash);
        UploadedByActorId = Guard.AgainstNullOrWhiteSpace(uploadedByActorId);
        UploadedAtUtc = uploadedAtUtc;
    }
}

