namespace Rge.BuildingBlocks.SharedKernel.Primitives;

/// <summary>
/// Abstraction over system time to allow deterministic testing of SLAs,
/// OAuth expiry, and Philippine business day calculations.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

