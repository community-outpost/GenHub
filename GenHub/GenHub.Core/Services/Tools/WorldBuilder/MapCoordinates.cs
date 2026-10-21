// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Single home for map cell and world coordinate conversion. Ports the engine
/// ADJUST_FROM_INDEX_TO_REAL convention: world feet are border-relative,
/// world = (index - borderSize) * cellSize, matching MapObject::getLocation
/// and the terrain vertex layout.
/// </summary>
public static class MapCoordinates
{
    /// <summary>
    /// Converts a cell corner to border-relative world feet.
    /// </summary>
    /// <param name="borderSize">The map border in cells.</param>
    /// <param name="cellX">The cell X.</param>
    /// <param name="cellY">The cell Y.</param>
    /// <returns>The world position in feet.</returns>
    public static (float X, float Y) CellToWorld(int borderSize, int cellX, int cellY)
    {
        return (
            (cellX - borderSize) * WorldBuilderConstants.Terrain.CellSize,
            (cellY - borderSize) * WorldBuilderConstants.Terrain.CellSize);
    }

    /// <summary>
    /// Converts a cell center to border-relative world feet.
    /// </summary>
    /// <param name="borderSize">The map border in cells.</param>
    /// <param name="cellX">The cell X.</param>
    /// <param name="cellY">The cell Y.</param>
    /// <returns>The world position in feet.</returns>
    public static (float X, float Y) CellCenterToWorld(int borderSize, int cellX, int cellY)
    {
        var corner = CellToWorld(borderSize, cellX, cellY);
        var half = WorldBuilderConstants.Terrain.CellSize / 2.0f;
        return (corner.X + half, corner.Y + half);
    }

    /// <summary>
    /// Converts border-relative world feet to clamped map cells.
    /// </summary>
    /// <param name="borderSize">The map border in cells.</param>
    /// <param name="x">The world X in feet.</param>
    /// <param name="y">The world Y in feet.</param>
    /// <param name="mapWidth">The map width in cells.</param>
    /// <param name="mapHeight">The map height in cells.</param>
    /// <returns>The clamped cell coordinates.</returns>
    public static (int X, int Y) WorldToCell(int borderSize, float x, float y, int mapWidth, int mapHeight)
    {
        var cellX = (int)Math.Round((x / WorldBuilderConstants.Terrain.CellSize) + borderSize);
        var cellY = (int)Math.Round((y / WorldBuilderConstants.Terrain.CellSize) + borderSize);
        return (Math.Clamp(cellX, 0, Math.Max(0, mapWidth - 1)), Math.Clamp(cellY, 0, Math.Max(0, mapHeight - 1)));
    }

    /// <summary>
    /// Samples terrain height in feet at a world position using bilinear interpolation.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="worldX">The world X in feet.</param>
    /// <param name="worldY">The world Y in feet.</param>
    /// <returns>The ground height in feet.</returns>
    public static float SampleGroundHeight(MapTerrainData terrain, float worldX, float worldY)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Heights.Count == 0)
        {
            return 0.0f;
        }

        var cellX = (worldX / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize;
        var cellY = (worldY / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize;
        var x = Math.Clamp(cellX, 0.0f, terrain.Width - 1.001f);
        var y = Math.Clamp(cellY, 0.0f, terrain.Height - 1.001f);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, terrain.Width - 1);
        var y1 = Math.Min(y0 + 1, terrain.Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var h00 = terrain.Heights[(y0 * terrain.Width) + x0];
        var h10 = terrain.Heights[(y0 * terrain.Width) + x1];
        var h01 = terrain.Heights[(y1 * terrain.Width) + x0];
        var h11 = terrain.Heights[(y1 * terrain.Width) + x1];
        var h0 = h00 + ((h10 - h00) * fx);
        var h1 = h01 + ((h11 - h01) * fx);
        return (h0 + ((h1 - h0) * fy)) * WorldBuilderConstants.Terrain.HeightScale;
    }
}
