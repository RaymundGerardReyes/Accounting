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
}

