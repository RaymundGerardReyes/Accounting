using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Documents.Domain;

public static class FileSizePolicy
{
    public const long MaxAllowedBytes = 10 * 1024 * 1024; // 10 MB

    public static Result ValidateSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return Result.Failure(Error.Validation("Documents.EmptyFile", "Uploaded file cannot be empty (0 bytes)."));
        }

        if (sizeBytes > MaxAllowedBytes)
        {
            return Result.Failure(Error.Validation(
                "Documents.FileTooLarge",
                $"Uploaded file ({sizeBytes} bytes) exceeds the 10 MB ({MaxAllowedBytes} bytes) limit."));
        }

        return Result.Success();
    }
}

