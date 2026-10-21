namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Classic Perlin noise with a seeded permutation table. Ports WBPerlinNoise:
/// Fisher-Yates permutation, octave summation with clamping, improved fade.
/// </summary>
public sealed class WbPerlinNoise
{
    private const int PermSize = 256;
    private readonly int[] permutation = new int[PermSize * 2];

    /// <summary>
    /// Initializes a new instance of the <see cref="WbPerlinNoise"/> class.
    /// </summary>
    /// <param name="seed">The permutation seed.</param>
    public WbPerlinNoise(int seed)
    {
        Permutate(seed);
    }

    /// <summary>Gets or sets the base amplitude.</summary>
    public float Amplitude { get; set; } = 1.0f;

    /// <summary>Gets or sets the base frequency.</summary>
    public float Frequency { get; set; } = 0.05f;

    /// <summary>Gets or sets the per-octave amplitude fade.</summary>
    public float Persistence { get; set; } = 0.5f;

    /// <summary>Gets or sets the octave count.</summary>
    public int Octaves { get; set; } = 4;

    /// <summary>Computes clamped [0, 1] noise at a 3D point.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z slice.</param>
    /// <returns>The noise value.</returns>
    public float Compute(float x, float y, float z)
    {
        var total = 0.0f;
        var amplitude = Amplitude;
        var frequency = Frequency;
        for (var i = 0; i < Octaves; i++)
        {
            total += Noise(x * frequency, y * frequency, z * frequency) * amplitude;
            frequency *= 2.0f;
            amplitude *= Persistence;
        }

        return Math.Clamp(total, 0.0f, 1.0f);
    }

    private static float Grad(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = z;
        if (h < 4)
        {
            v = y;
        }
        else if (h == 12 || h == 14)
        {
            v = x;
        }

        var first = ((h & 1) == 0) ? u : -u;
        var second = ((h & 2) == 0) ? v : -v;
        return first + second;
    }

    private static float Fade(float t)
    {
        var square = t * t;
        var cube = square * t;
        var scaled = (t * 6.0f) - 15.0f;
        var grown = t * scaled;
        var curved = grown + 10.0f;
        return cube * curved;
    }

    private static float Lerp(float alpha, float a, float b)
    {
        return a + (alpha * (b - a));
    }

    private float Noise(float x, float y, float z)
    {
        var ix = (int)Math.Floor(x) & 255;
        var iy = (int)Math.Floor(y) & 255;
        var iz = (int)Math.Floor(z) & 255;
        x -= (float)Math.Floor(x);
        y -= (float)Math.Floor(y);
        z -= (float)Math.Floor(z);
        var u = Fade(x);
        var v = Fade(y);
        var w = Fade(z);
        var a = permutation[ix] + iy;
        var aa = permutation[a] + iz;
        var ab = permutation[a + 1] + iz;
        var b = permutation[ix + 1] + iy;
        var ba = permutation[b] + iz;
        var bb = permutation[b + 1] + iz;
        return Lerp(
            w,
            Lerp(
                v,
                Lerp(u, Grad(permutation[aa], x, y, z), Grad(permutation[ba], x - 1, y, z)),
                Lerp(u, Grad(permutation[ab], x, y - 1, z), Grad(permutation[bb], x - 1, y - 1, z))),
            Lerp(
                v,
                Lerp(u, Grad(permutation[aa + 1], x, y, z - 1), Grad(permutation[ba + 1], x - 1, y, z - 1)),
                Lerp(u, Grad(permutation[ab + 1], x, y - 1, z - 1), Grad(permutation[bb + 1], x - 1, y - 1, z - 1))));
    }

    private void Permutate(int seed)
    {
        for (var i = 0; i < PermSize; i++)
        {
            permutation[i] = i;
        }

        var random = new WbRandom((uint)seed);
        for (var i = PermSize - 1; i > 0; i--)
        {
            var j = random.NextInt(i + 1);
            (permutation[i], permutation[j]) = (permutation[j], permutation[i]);
        }

        for (var i = 0; i < PermSize; i++)
        {
            permutation[PermSize + i] = permutation[i];
        }
    }
}
