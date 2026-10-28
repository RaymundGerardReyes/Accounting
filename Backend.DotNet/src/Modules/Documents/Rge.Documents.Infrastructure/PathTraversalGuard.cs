using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Documents.Infrastructure;

public static class PathTraversalGuard
{
    public static Result EnsureSafeFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return Result.Failure(Error.Validation("Documents.EmptyFilename", "Filename cannot be empty."));
        }

        if (filename.Contains("..", StringComparison.Ordinal) ||
            filename.Contains('/', StringComparison.Ordinal) ||
            filename.Contains('\\', StringComparison.Ordinal) ||
            filename.Contains('\0', StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation(
                "Documents.PathTraversalDetected",
                "Filename contains illegal path traversal characters."));
        }

        return Result.Success();
    }

    public static string GetSafeVaultFilePath(string vaultRootDirectory, string? candidateFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootDirectory);

        Result check = EnsureSafeFilename(candidateFileName);
        if (check.IsFailure)
        {
            throw new UnauthorizedAccessException(check.Error.Message);
        }

        string fullVaultRoot = Path.GetFullPath(vaultRootDirectory);
        string combined = Path.GetFullPath(Path.Combine(fullVaultRoot, candidateFileName!));

        if (!combined.StartsWith(fullVaultRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Resolved file path escapes the document vault root directory.");
        }

        return combined;
    }
}

