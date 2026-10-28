using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Documents.Domain;

public sealed class MimeSignature : ValueObject
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public string MimeType { get; }
    public string Extension { get; }

    public MimeSignature(string mimeType, string extension)
    {
        MimeType = Guard.AgainstNullOrWhiteSpace(mimeType).ToLowerInvariant();
        Extension = Guard.AgainstNullOrWhiteSpace(extension).ToLowerInvariant();
    }

    public static Result<MimeSignature> DetectAndValidate(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.StartsWith(PdfMagic))
        {
            return Result.Success(new MimeSignature("application/pdf", ".pdf"));
        }

        if (headerBytes.StartsWith(PngMagic))
        {
            return Result.Success(new MimeSignature("image/png", ".png"));
        }

        if (headerBytes.StartsWith(JpegMagic))
        {
            return Result.Success(new MimeSignature("image/jpeg", ".jpg"));
        }

        return Result.Failure<MimeSignature>(
            Error.Validation("Documents.UnsupportedMagicBytes", "Only genuine PDF, PNG, and JPEG files are permitted."));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MimeType;
        yield return Extension;
    }
}


