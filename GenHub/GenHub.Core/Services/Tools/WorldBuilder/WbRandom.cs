namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Seeded xorshift32 random stream. Ports WBRandom exactly:
/// xorshift32 core, nextInt via shifted modulo, 24-bit nextReal.
/// </summary>
public sealed class WbRandom
{
    private uint state;

    /// <summary>
    /// Initializes a new instance of the <see cref="WbRandom"/> class.
    /// </summary>
    /// <param name="seed">The seed. Zero maps to a fixed nonzero state.</param>
    public WbRandom(uint seed)
    {
        state = seed != 0 ? seed : 0x9E3779B9u;
    }

    /// <summary>Draws the next raw value.</summary>
    /// <returns>The value.</returns>
    public uint Next()
    {
        // xorshift32 xors the state with its own shifted value; a shift-assignment
        // would compute a different stream, so keep the self-referential form.
        state ^= state << 13; // skipcq: CS-R1100
        state ^= state >> 17; // skipcq: CS-R1100
        state ^= state << 5; // skipcq: CS-R1100
        return state;
    }

    /// <summary>Draws a value in [0, limit).</summary>
    /// <param name="limit">Exclusive upper bound.</param>
    /// <returns>The value, or 0 when the limit is not positive.</returns>
    public int NextInt(int limit)
    {
        if (limit <= 0)
        {
            return 0;
        }

        return (int)((Next() >> 1) % (uint)limit);
    }

    /// <summary>Draws a value in [lo, hi).</summary>
    /// <param name="lo">Inclusive lower bound.</param>
    /// <param name="hi">Exclusive upper bound.</param>
    /// <returns>The value, or lo when the range is empty.</returns>
    public int NextRange(int lo, int hi)
    {
        if (hi <= lo)
        {
            return lo;
        }

        return lo + NextInt(hi - lo);
    }

    /// <summary>Draws a value in [0, 1).</summary>
    /// <returns>The value.</returns>
    public float NextReal()
    {
        return (Next() >> 8) / (float)(1 << 24);
    }
}
