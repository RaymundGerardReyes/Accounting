using System.Threading;

namespace Rge.Tests.Common.Generators;

/// <summary>
/// Deterministic sequential ID generator for predictable, isolated test execution.
/// </summary>
public sealed class FakeIdGenerator
{
    private long _counter;

    public FakeIdGenerator(long initialValue = 0)
    {
        _counter = initialValue;
    }

    public string NextId(string prefix = "id")
    {
        var val = Interlocked.Increment(ref _counter);
        return $"{prefix}-{val:D6}";
    }

    public void Reset(long initialValue = 0)
    {
        Interlocked.Exchange(ref _counter, initialValue);
    }
}
