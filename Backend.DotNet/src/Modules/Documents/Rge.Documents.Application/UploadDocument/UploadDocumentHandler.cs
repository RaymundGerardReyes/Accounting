using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Application.Ports;
using Rge.Documents.Domain;

namespace Rge.Documents.Application.UploadDocument;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary file payload")]
public sealed record UploadDocumentCommand(
    string OriginalFilename,
    byte[] FileBytes,
    string UploadedByActorId) : ICommand<DocumentMetadataDto>;

public interface IMagicByteInspector
{
    Result<(string DetectedMimeType, string SafeStoredFilename)> Inspect(string originalFilename, ReadOnlySpan<byte> fileBytes);
    string ComputeSha256(ReadOnlySpan<byte> fileBytes);
}

public sealed class UploadDocumentHandler : ICommandHandler<UploadDocumentCommand, DocumentMetadataDto>
{
    private readonly IMagicByteInspector _inspector;
    private readonly IAntivirusScanner _antivirusScanner;
    private readonly IDocumentVaultStore _vaultStore;
    private readonly IClock _clock;

    public UploadDocumentHandler(
        IMagicByteInspector inspector,
        IAntivirusScanner antivirusScanner,
        IDocumentVaultStore vaultStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(antivirusScanner);
        ArgumentNullException.ThrowIfNull(vaultStore);
        ArgumentNullException.ThrowIfNull(clock);

        _inspector = inspector;
        _antivirusScanner = antivirusScanner;
        _vaultStore = vaultStore;
        _clock = clock;
    }

    public async Task<Result<DocumentMetadataDto>> HandleAsync(
        UploadDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FileBytes is null)
        {
            return Result.Failure<DocumentMetadataDto>(
                Error.Validation("Documents.MissingContent", "File bytes are required."));
        }

        Result sizeCheck = FileSizePolicy.ValidateSize(command.FileBytes.LongLength);
        if (sizeCheck.IsFailure)
        {
            return Result.Failure<DocumentMetadataDto>(sizeCheck.Error);
        }

        var magicResult = _inspector.Inspect(command.OriginalFilename, command.FileBytes);
        if (magicResult.IsFailure)
        {
            return Result.Failure<DocumentMetadataDto>(magicResult.Error);
        }

        Result avResult = await _antivirusScanner
            .ScanBytesAsync(command.FileBytes, command.OriginalFilename, cancellationToken)
            .ConfigureAwait(false);

        if (avResult.IsFailure)
        {
            return Result.Failure<DocumentMetadataDto>(avResult.Error);
        }

        string sha256 = _inspector.ComputeSha256(command.FileBytes);
        var doc = new DocumentReference(
            id: Guid.NewGuid().ToString("D"),
            originalFilename: command.OriginalFilename.Trim(),
            storedFilename: magicResult.Value.SafeStoredFilename,
            mimeType: magicResult.Value.DetectedMimeType,
            sizeBytes: command.FileBytes.LongLength,
            sha256Hash: sha256,
            uploadedByActorId: string.IsNullOrWhiteSpace(command.UploadedByActorId) ? "system" : command.UploadedByActorId,
            uploadedAtUtc: _clock.UtcNow);

        await _vaultStore.SaveDocumentAsync(doc, command.FileBytes, cancellationToken).ConfigureAwait(false);

        return Result.Success(new DocumentMetadataDto(
            doc.Id,
            doc.OriginalFilename,
            doc.StoredFilename,
            doc.MimeType,
            doc.SizeBytes,
            doc.Sha256Hash,
            doc.UploadedByActorId,
            doc.UploadedAtUtc));
    }
}
