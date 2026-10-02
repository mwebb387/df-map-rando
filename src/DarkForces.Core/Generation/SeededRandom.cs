namespace DarkForces.Core.Generation;

/// <summary>
/// xoshiro256** seeded by SplitMix64. Implemented here rather than using System.Random so the sequence for a seed
/// never changes between .NET versions — generated levels must stay reproducible from their settings string.
/// </summary>
public sealed class SeededRandom
{
    ulong _s0, _s1, _s2, _s3;

    public SeededRandom(ulong seed)
    {
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    static ulong SplitMix(ref ulong x)
    {
        var z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        var result = Rotl(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [0, n) without modulo bias.</summary>
    public int Next(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        var bound = (ulong)n;
        var limit = ulong.MaxValue - ulong.MaxValue % bound;
        ulong r;
        do r = NextUInt64(); while (r >= limit);
        return (int)(r % bound);
    }

    /// <summary>Uniform double in [0, 1) from the top 53 bits.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    /// <summary>Fisher–Yates shuffle into a new list.</summary>
    public List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
