using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Application.Ports;

namespace Rge.Documents.Application.DownloadDocument;

public sealed record DownloadDocumentQuery(string DocumentId) : IQuery<DownloadedDocumentDto>;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary document content payload")]
public sealed record DownloadedDocumentDto(
    DocumentMetadataDto Metadata,
    byte[] Content);

public sealed class DownloadDocumentHandler : IQueryHandler<DownloadDocumentQuery, DownloadedDocumentDto>
{
    private readonly IDocumentVaultStore _vaultStore;

    public DownloadDocumentHandler(IDocumentVaultStore vaultStore)
    {
        ArgumentNullException.ThrowIfNull(vaultStore);
        _vaultStore = vaultStore;
    }

    public async Task<Result<DownloadedDocumentDto>> HandleAsync(
        DownloadDocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.DocumentId))
        {
            return Result.Failure<DownloadedDocumentDto>(
                Error.Validation("Documents.MissingId", "Document ID is required."));
        }

        var found = await _vaultStore.GetDocumentAsync(query.DocumentId.Trim(), cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return Result.Failure<DownloadedDocumentDto>(
                Error.NotFound("Documents.NotFound", $"Document '{query.DocumentId}' was not found."));
        }

        return Result.Success(new DownloadedDocumentDto(found.Value.Metadata, found.Value.Content));
    }
}
