using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Tests.Common.Time;

/// <summary>
/// Settable, deterministic clock implementation for tests pinned to Asia/Manila by default.
/// </summary>
public sealed class FakeClock : IClock
{
    private static readonly TimeZoneInfo ManilaZone =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

    public DateTimeOffset UtcNow { get; set; }

    public FakeClock() : this(new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero))
    {
    }

    public FakeClock(DateTimeOffset initialTimeUtc)
    {
        UtcNow = initialTimeUtc;
    }

    public void Advance(TimeSpan delta)
    {
        UtcNow = UtcNow.Add(delta);
    }

    public void SetTime(DateTimeOffset timeUtc)
    {
        UtcNow = timeUtc;
    }

    public DateTimeOffset NowInManila()
    {
        return TimeZoneInfo.ConvertTime(UtcNow, ManilaZone);
    }
}

