// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Terrain texture-coordinate math. Ports WorldHeightMap::getUVForNdx (tile
/// slot plus the 64px-tile quadrant selected by the low two tile bits),
/// getUVForTileIndex (four cell corners plus the cliff UV override when the
/// cliff record's class matches the cell class), and getAlphaUVData (the
/// 16-direction per-corner alpha table with flip tracking). The steep-slope
/// old-UV stretch heuristic is deferred with cliff UV authoring: it only
/// triggers on cliff-grade height deltas.
/// </summary>
public static class WbTerrainUv
{
    /// <summary>
    /// Tile pixel extent in the atlas.
    /// </summary>
    public const int TilePixelExtent = 64;

    /// <summary>
    /// Computes the atlas UV rect for one tile index, including quadrant
    /// selection: bit 1 flips V, bit 0 flips U, because a 64px tile spans a
    /// 2x2 cell block while the height grid samples 32x32 quadrants.
    /// </summary>
    /// <param name="atlas">The tile atlas layout.</param>
    /// <param name="tileNdx">The tile index.</param>
    /// <param name="minU">The minimum U.</param>
    /// <param name="minV">The minimum V.</param>
    /// <param name="maxU">The maximum U.</param>
    /// <param name="maxV">The maximum V.</param>
    /// <returns>False when the tile has no atlas slot (missing texture).</returns>
    public static bool GetTileUv(WbTileAtlas atlas, int tileNdx, out float minU, out float minV, out float maxU, out float maxV)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        minU = minV = maxU = maxV = 0.0f;
        if (!atlas.TileUv.TryGetValue(tileNdx >> 2, out var rect))
        {
            return false;
        }

        minU = rect.MinU;
        minV = rect.MinV;
        maxU = rect.MaxU;
        maxV = rect.MaxV;
        var midU = (minU + maxU) / 2.0f;
        var midV = (minV + maxV) / 2.0f;
        if ((tileNdx & 2) != 0)
        {
            maxV = midV;
        }
        else
        {
            minV = midV;
        }

        if ((tileNdx & 1) != 0)
        {
            minU = midU;
        }
        else
        {
            maxU = midU;
        }

        return true;
    }

    /// <summary>
    /// Fills the four cell-corner UVs as U={nU,xU,xU,nU}, V={xV,xV,nV,nV},
    /// applying the cliff UV override when the cliff record's tile class
    /// matches the cell's class.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="atlas">The tile atlas layout.</param>
    /// <param name="x">The cell X.</param>
    /// <param name="y">The cell Y.</param>
    /// <param name="u">The four corner U values.</param>
    /// <param name="v">The four corner V values.</param>
    /// <returns>The flip flag and whether a texture was found.</returns>
    public static (bool Flip, bool HasTexture) GetCellUv(MapTerrainData terrain, WbTileAtlas atlas, int x, int y, float[] u, float[] v)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(v);
        var index = (y * terrain.Width) + x;
        if (!GetTileUv(atlas, terrain.TileIndices[index], out var minU, out var minV, out var maxU, out var maxV))
        {
            u[0] = u[1] = u[2] = u[3] = 0.0f;
            v[0] = v[1] = v[2] = v[3] = 0.0f;
            return (false, false);
        }

        u[0] = minU;
        u[1] = maxU;
        u[2] = maxU;
        u[3] = minU;
        v[0] = maxV;
        v[1] = maxV;
        v[2] = minV;
        v[3] = minV;
        if (minU == 0.0f)
        {
            return (false, false);
        }

        if (terrain.CliffInfoIndices[index] == 0 ||
            terrain.CliffInfoIndices[index] >= terrain.CliffInfos.Count)
        {
            return (false, true);
        }

        var info = terrain.CliffInfos[terrain.CliffInfoIndices[index]];
        var cellClass = MapTerrainTools.GetTextureClassFromNdx(terrain, terrain.TileIndices[index]);
        var cliffClass = MapTerrainTools.GetTextureClassFromNdx(terrain, info.TileIndex);
        if (cellClass < 0 || cellClass != cliffClass || info.U.Count < 8 ||
            !atlas.ClassUv.TryGetValue(terrain.TextureClasses[cellClass].Name, out var slot))
        {
            return (false, true);
        }

        var minSlotU = slot.MinU;
        var maxSlotV = slot.MaxV;
        var vFactor = atlas.Width / (float)Math.Max(1, atlas.Height);
        u[0] = info.U[0] + minSlotU;
        u[1] = info.U[2] + minSlotU;
        u[2] = info.U[4] + minSlotU;
        u[3] = info.U[6] + minSlotU;
        v[0] = (info.U[1] * vFactor) + maxSlotV;
        v[1] = (info.U[3] * vFactor) + maxSlotV;
        v[2] = (info.U[5] * vFactor) + maxSlotV;
        v[3] = (info.U[7] * vFactor) + maxSlotV;
        return (info.Flip != 0, true);
    }

    /// <summary>
    /// Fills the base UVs plus the per-corner blend alpha bytes for one cell.
    /// Blended cells sample the blend record's base tile; unblended cells
    /// emit zero alpha. A cliff stretch recomputes the flip from diagonal
    /// height deltas.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="atlas">The tile atlas layout.</param>
    /// <param name="x">The cell X.</param>
    /// <param name="y">The cell Y.</param>
    /// <param name="u">The four corner U values.</param>
    /// <param name="v">The four corner V values.</param>
    /// <param name="alpha">The four corner alpha bytes.</param>
    /// <returns>Whether the cell diagonal needs flipping.</returns>
    public static bool GetCellAlpha(MapTerrainData terrain, WbTileAtlas atlas, int x, int y, float[] u, float[] v, byte[] alpha)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(atlas);
        var index = (y * terrain.Width) + x;
        var blendIndex = terrain.BlendTileIndices[index];
        var stretchedForCliff = false;
        var needFlip = false;
        if (blendIndex == 0 || blendIndex >= terrain.BlendTiles.Count)
        {
            var (flip, _) = GetCellUv(terrain, atlas, x, y, u, v);
            stretchedForCliff = flip;
            needFlip = false;
            ClearAlpha(alpha);
        }
        else
        {
            var record = terrain.BlendTiles[blendIndex];
            stretchedForCliff = SampleBlendUv(terrain, atlas, index, record.BlendIndex, u, v);
            needFlip = ApplyBlendAlpha(record, alpha);
        }

        if (stretchedForCliff)
        {
            needFlip = CliffDiagonalFlip(terrain.Heights, terrain.Width, index);
        }

        return needFlip;
    }

    private static bool ApplyBlendAlpha(MapBlendTile record, byte[] alpha)
    {
        ClearAlpha(alpha);
        var needFlip = ApplyHorizontalAlpha(record, alpha);
        if (record.Vertical != 0)
        {
            needFlip = ApplyVerticalAlpha(record, alpha);
        }

        if (ApplyRightDiagonalAlpha(record, alpha))
        {
            needFlip = true;
        }

        if (ApplyLeftDiagonalAlpha(record, alpha))
        {
            needFlip = true;
        }

        if (record.CustomBlendEdgeClass < 0)
        {
            return needFlip;
        }

        ClearAlpha(alpha);
        return false;
    }

    private static bool ApplyHorizontalAlpha(MapBlendTile record, byte[] alpha)
    {
        if (record.Horizontal == 0)
        {
            return false;
        }

        if ((record.Inverted & WorldBuilderConstants.Limits.InvertedMask) != 0)
        {
            alpha[0] = alpha[3] = 255;
        }
        else
        {
            alpha[1] = alpha[2] = 255;
        }

        return (record.Inverted & WorldBuilderConstants.Limits.FlippedMask) != 0;
    }

    private static bool ApplyVerticalAlpha(MapBlendTile record, byte[] alpha)
    {
        if ((record.Inverted & WorldBuilderConstants.Limits.InvertedMask) != 0)
        {
            alpha[0] = alpha[1] = 255;
        }
        else
        {
            alpha[2] = alpha[3] = 255;
        }

        return (record.Inverted & WorldBuilderConstants.Limits.FlippedMask) != 0;
    }

    private static bool ApplyRightDiagonalAlpha(MapBlendTile record, byte[] alpha)
    {
        if (record.RightDiagonal == 0)
        {
            return false;
        }

        if ((record.Inverted & WorldBuilderConstants.Limits.InvertedMask) != 0)
        {
            alpha[1] = 255;
            if (record.LongDiagonal != 0)
            {
                alpha[0] = 255;
                alpha[2] = 255;
            }

            return false;
        }

        alpha[2] = 255;
        if (record.LongDiagonal != 0)
        {
            alpha[1] = 255;
            alpha[3] = 255;
        }

        return true;
    }

    private static bool ApplyLeftDiagonalAlpha(MapBlendTile record, byte[] alpha)
    {
        if (record.LeftDiagonal == 0)
        {
            return false;
        }

        if ((record.Inverted & WorldBuilderConstants.Limits.InvertedMask) != 0)
        {
            alpha[0] = 255;
            if (record.LongDiagonal != 0)
            {
                alpha[1] = 255;
                alpha[3] = 255;
            }

            return true;
        }

        alpha[3] = 255;
        if (record.LongDiagonal != 0)
        {
            alpha[0] = 255;
            alpha[2] = 255;
        }

        return false;
    }

    private static bool CliffDiagonalFlip(IList<byte> heights, int width, int index)
    {
        if (width <= 0 || index < 0 || index + width + 1 >= heights.Count || (index % width) == width - 1)
        {
            return false;
        }

        var p0 = heights[index];
        var p1 = heights[index + 1];
        var p2 = heights[index + width + 1];
        var p3 = heights[index + width];
        return Math.Abs(p0 - p2) > Math.Abs(p1 - p3);
    }

    private static void ClearAlpha(byte[] alpha)
    {
        alpha[0] = alpha[1] = alpha[2] = alpha[3] = 0;
    }

    private static bool SampleBlendUv(MapTerrainData terrain, WbTileAtlas atlas, int index, int blendTileNdx, float[] u, float[] v)
    {
        if (!GetTileUv(atlas, blendTileNdx, out var minU, out var minV, out var maxU, out var maxV))
        {
            u[0] = u[1] = u[2] = u[3] = 0.0f;
            v[0] = v[1] = v[2] = v[3] = 0.0f;
            return false;
        }

        u[0] = minU;
        u[1] = maxU;
        u[2] = maxU;
        u[3] = minU;
        v[0] = maxV;
        v[1] = maxV;
        v[2] = minV;
        v[3] = minV;
        if (minU == 0.0f)
        {
            return false;
        }

        if (terrain.CliffInfoIndices[index] == 0 || terrain.CliffInfoIndices[index] >= terrain.CliffInfos.Count)
        {
            return false;
        }

        var info = terrain.CliffInfos[terrain.CliffInfoIndices[index]];
        var cellClass = MapTerrainTools.GetTextureClassFromNdx(terrain, terrain.TileIndices[index]);
        var cliffClass = MapTerrainTools.GetTextureClassFromNdx(terrain, blendTileNdx);
        if (cellClass < 0 || cellClass != cliffClass || info.U.Count < 8 ||
            !atlas.ClassUv.TryGetValue(terrain.TextureClasses[cellClass].Name, out var slot))
        {
            return false;
        }

        var vFactor = atlas.Width / (float)Math.Max(1, atlas.Height);
        u[0] = info.U[0] + slot.MinU;
        u[1] = info.U[2] + slot.MinU;
        u[2] = info.U[4] + slot.MinU;
        u[3] = info.U[6] + slot.MinU;
        v[0] = (info.U[1] * vFactor) + slot.MaxV;
        v[1] = (info.U[3] * vFactor) + slot.MaxV;
        v[2] = (info.U[5] * vFactor) + slot.MaxV;
        v[3] = (info.U[7] * vFactor) + slot.MaxV;
        return info.Flip != 0;
    }
}
