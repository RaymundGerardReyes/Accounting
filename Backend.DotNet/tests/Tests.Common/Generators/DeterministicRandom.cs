using System;
using System.Diagnostics.CodeAnalysis;

namespace Rge.Tests.Common.Generators;

/// <summary>
/// Seeded pseudorandom generator that explicitly records its seed for test reproducibility.
/// </summary>
public sealed class DeterministicRandom
{
    private readonly Random _random;

    public int Seed { get; }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded random for unit test reproducibility")]
    public DeterministicRandom(int seed = 42)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded random for unit test reproducibility")]
    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded random for unit test reproducibility")]
    public long NextLong(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        var uRange = (ulong)(maxValue - minValue);
        var buf = new byte[8];
        _random.NextBytes(buf);
        var uLongRand = BitConverter.ToUInt64(buf, 0);
        return (long)(minValue + (long)(uLongRand % uRange));
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded random for unit test reproducibility")]
    public string NextAlphaNumeric(int length = 16)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var buffer = new char[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = chars[_random.Next(chars.Length)];
        }
        return new string(buffer);
    }
}

