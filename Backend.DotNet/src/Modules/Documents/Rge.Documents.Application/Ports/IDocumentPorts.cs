using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Domain;

namespace Rge.Documents.Application.Ports;

public sealed record DocumentMetadataDto(
    string Id,
    string OriginalFilename,
    string StoredFilename,
    string MimeType,
    long SizeBytes,
    string Sha256Hash,
    string UploadedByActorId,
    DateTimeOffset UploadedAtUtc);

public interface IDocumentVaultStore
{
    Task SaveDocumentAsync(DocumentReference document, byte[] content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentMetadataDto>> ListDocumentsAsync(CancellationToken cancellationToken = default);
    Task<(DocumentMetadataDto Metadata, byte[] Content)?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
}

public interface IAntivirusScanner
{
    Task<Result> ScanBytesAsync(ReadOnlyMemory<byte> fileBytes, string filename, CancellationToken cancellationToken = default);
}

