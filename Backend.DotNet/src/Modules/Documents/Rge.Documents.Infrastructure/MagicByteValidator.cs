using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Application.UploadDocument;
using Rge.Documents.Domain;

namespace Rge.Documents.Infrastructure;

public sealed class MagicByteInspectorAdapter : IMagicByteInspector
{
    public Result<(string DetectedMimeType, string SafeStoredFilename)> Inspect(
        string originalFilename,
        ReadOnlySpan<byte> fileBytes) =>
        MagicByteValidator.ValidateAndCreateSafeName(originalFilename, fileBytes.Length, fileBytes);

    public string ComputeSha256(ReadOnlySpan<byte> fileBytes) =>
        Sha256Hasher.ComputeHex(fileBytes);
}

public static class MagicByteValidator
{
    public const long MaxFileSizeBytes = FileSizePolicy.MaxAllowedBytes;

    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public static Result<(string DetectedMimeType, string SafeStoredFilename)> ValidateAndCreateSafeName(
        string? originalFilename,
        long sizeBytes,
        ReadOnlySpan<byte> headerBytes)
    {
        Result pathCheck = PathTraversalGuard.EnsureSafeFilename(originalFilename);
        if (pathCheck.IsFailure)
        {
            return Result.Failure<(string, string)>(pathCheck.Error);
        }

        Result sizeCheck = FileSizePolicy.ValidateSize(sizeBytes);
        if (sizeCheck.IsFailure)
        {
            return Result.Failure<(string, string)>(sizeCheck.Error);
        }

        if (headerBytes.StartsWith(PdfMagic))
        {
            return Result.Success(("application/pdf", $"{Guid.NewGuid():N}.pdf"));
        }

        if (headerBytes.StartsWith(PngMagic))
        {
            return Result.Success(("image/png", $"{Guid.NewGuid():N}.png"));
        }

        if (headerBytes.StartsWith(JpegMagic))
        {
            return Result.Success(("image/jpeg", $"{Guid.NewGuid():N}.jpg"));
        }

        return Result.Failure<(string, string)>(
            Error.Validation("Documents.UnsupportedMagicBytes", "Only genuine PDF, PNG, and JPEG files are permitted."));
    }
}

