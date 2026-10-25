using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Documents.Infrastructure;

/// <summary>
/// Enforces strict magic-byte signature verification (%PDF-, PNG, JPEG),
/// the 10 MB file size cap, and path-traversal-safe storage filenames.
/// </summary>
public static class MagicByteValidator
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public static Result<(string DetectedMimeType, string SafeStoredFilename)> ValidateAndCreateSafeName(
        string? originalFilename,
        long sizeBytes,
        ReadOnlySpan<byte> headerBytes)
    {
        if (string.IsNullOrWhiteSpace(originalFilename))
        {
            return Result.Failure<(string, string)>(
                Error.Validation("Documents.EmptyFilename", "Original filename is required."));
        }

        if (originalFilename.Contains("..", StringComparison.Ordinal) ||
            originalFilename.Contains('/', StringComparison.Ordinal) ||
            originalFilename.Contains('\\', StringComparison.Ordinal))
        {
            return Result.Failure<(string, string)>(
                Error.Validation("Documents.PathTraversalDetected", "Filename contains illegal path characters."));
        }

        if (sizeBytes <= 0 || sizeBytes > MaxFileSizeBytes)
        {
            return Result.Failure<(string, string)>(
                Error.Validation("Documents.InvalidFileSize", $"File size must be between 1 byte and {MaxFileSizeBytes} bytes (10 MB)."));
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

