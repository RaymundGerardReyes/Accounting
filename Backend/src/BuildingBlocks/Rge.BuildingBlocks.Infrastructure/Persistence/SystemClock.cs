using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Default system clock returning DateTimeOffset.UtcNow.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

