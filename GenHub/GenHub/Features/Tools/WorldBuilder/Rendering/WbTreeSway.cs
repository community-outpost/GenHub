// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Precomputed canopy sway cycle from W3DTreeBuffer: over one wind cycle the
/// lateral amplitude follows a sine and the vertical bob follows cosine minus
/// one. Each tree carries a phase and a wind direction; the per-frame offset
/// is the direction scaled by the sine amplitude plus the vertical bob.
/// Trees render at phase zero until model animation is enabled.
/// </summary>
public static class WbTreeSway
{
    /// <summary>
    /// Sway table entries per wind cycle.
    /// </summary>
    public const int EntryCount = 64;

    /// <summary>
    /// Gets the unit sway cycle at a phase entry: X is the lateral sine
    /// amplitude and Z is the vertical cosine-minus-one bob.
    /// </summary>
    /// <param name="phase">The phase entry, wrapped to the table.</param>
    /// <returns>The unit (amplitude, bob) pair.</returns>
    public static Vector2 CycleAt(int phase)
    {
        var angle = (WrappedPhase(phase) * 2.0f * MathF.PI) / EntryCount;
        return new Vector2(MathF.Sin(angle), MathF.Cos(angle) - 1.0f);
    }

    /// <summary>
    /// Applies the sway cycle along a wind direction.
    /// </summary>
    /// <param name="phase">The phase entry, wrapped to the table.</param>
    /// <param name="directionX">Wind direction X.</param>
    /// <param name="directionY">Wind direction Y.</param>
    /// <param name="amplitudeFeet">Sway amplitude in feet.</param>
    /// <returns>The XYZ offset in feet.</returns>
    public static Vector3 Displace(int phase, float directionX, float directionY, float amplitudeFeet)
    {
        var cycle = CycleAt(phase);
        return new Vector3(
            directionX * cycle.X * amplitudeFeet,
            directionY * cycle.X * amplitudeFeet,
            cycle.Y * amplitudeFeet);
    }

    private static int WrappedPhase(int phase)
    {
        return ((phase % EntryCount) + EntryCount) % EntryCount;
    }
}
