// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
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
}
