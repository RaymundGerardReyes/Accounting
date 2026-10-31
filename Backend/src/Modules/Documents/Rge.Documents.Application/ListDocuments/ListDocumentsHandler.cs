using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Application.Ports;

namespace Rge.Documents.Application.ListDocuments;

public sealed record ListDocumentsQuery : IQuery<IReadOnlyList<DocumentMetadataDto>>;

public sealed class ListDocumentsHandler : IQueryHandler<ListDocumentsQuery, IReadOnlyList<DocumentMetadataDto>>
{
    private readonly IDocumentVaultStore _vaultStore;

    public ListDocumentsHandler(IDocumentVaultStore vaultStore)
    {
        ArgumentNullException.ThrowIfNull(vaultStore);
        _vaultStore = vaultStore;
    }

    public async Task<Result<IReadOnlyList<DocumentMetadataDto>>> HandleAsync(
        ListDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = await _vaultStore.ListDocumentsAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(items);
    }
}

