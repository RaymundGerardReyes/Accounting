using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Tests.Common;

/// <summary>
/// Deterministic clock for testing 15-minute account lockouts, 10-minute OAuth PKCE state expiry,
/// 5-minute webhook replay windows, and Philippine business day SLAs.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; }

    public FakeClock(DateTimeOffset? initialUtc = null)
    {
        UtcNow = initialUtc ?? new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    }

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    public void SetUtcNow(DateTimeOffset value) => UtcNow = value;
}

