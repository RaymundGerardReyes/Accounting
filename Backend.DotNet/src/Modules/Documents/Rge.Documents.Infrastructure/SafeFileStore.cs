using System.Collections.Concurrent;
using Rge.Documents.Application.Ports;
using Rge.Documents.Domain;

namespace Rge.Documents.Infrastructure;

public sealed class SafeFileStore : IDocumentVaultStore
{
    private readonly ConcurrentDictionary<string, (DocumentMetadataDto Metadata, byte[] Bytes)> _vault =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SaveDocumentAsync(
        DocumentReference document,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);

        var metadata = new DocumentMetadataDto(
            document.Id,
            document.OriginalFilename,
            document.StoredFilename,
            document.MimeType,
            document.SizeBytes,
            document.Sha256Hash,
            document.UploadedByActorId,
            document.UploadedAtUtc);

        _vault[document.Id] = (metadata, [.. content]);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentMetadataDto>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DocumentMetadataDto> items = _vault.Values
            .Select(static v => v.Metadata)
            .OrderByDescending(static m => m.UploadedAtUtc)
            .ToList();

        return Task.FromResult(items);
    }

    public Task<(DocumentMetadataDto Metadata, byte[] Content)?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (_vault.TryGetValue(documentId, out var entry))
        {
            return Task.FromResult<(DocumentMetadataDto, byte[])?>((entry.Metadata, entry.Bytes));
        }

        return Task.FromResult<(DocumentMetadataDto, byte[])?>(null);
    }
}

