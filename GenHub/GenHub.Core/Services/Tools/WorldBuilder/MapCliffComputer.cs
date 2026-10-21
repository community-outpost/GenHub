using GenHub.Core.Constants;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Recomputes packed cliff-state bits from height bytes.
/// Ports WorldHeightMap.setCellCliffFlagFromHeights: a cell is cliff when its
/// 2x2 height range in world Z exceeds the slope limit.
/// </summary>
public static class MapCliffComputer
{
    /// <summary>
    /// Computes cliff-state bytes for a height field.
    /// </summary>
    /// <param name="heights">Height bytes row by row.</param>
    /// <param name="width">Width in cells.</param>
    /// <param name="height">Height in cells.</param>
    /// <param name="slopeLimitWorldZ">Cliff threshold in world Z units.</param>
    /// <returns>Packed cliff-state bytes.</returns>
    public static byte[] ComputeCliffState(IList<byte> heights, int width, int height, float slopeLimitWorldZ)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var stride = (width + 7) / 8;
        var state = new byte[stride * height];
        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var h1 = heights[(y * width) + x] * WorldBuilderConstants.Terrain.HeightScale;
                var h2 = heights[(y * width) + x + 1] * WorldBuilderConstants.Terrain.HeightScale;
                var h3 = heights[((y + 1) * width) + x] * WorldBuilderConstants.Terrain.HeightScale;
                var h4 = heights[((y + 1) * width) + x + 1] * WorldBuilderConstants.Terrain.HeightScale;
                var min = Math.Min(Math.Min(h1, h2), Math.Min(h3, h4));
                var max = Math.Max(Math.Max(h1, h2), Math.Max(h3, h4));
                if (max - min > slopeLimitWorldZ)
                {
                    state[(y * stride) + (x >> 3)] |= (byte)(1 << (x & 0x7));
                }
            }
        }

        return state;
    }
}
