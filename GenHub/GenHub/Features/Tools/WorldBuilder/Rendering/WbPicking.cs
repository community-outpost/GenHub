// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Screen-to-world picking for the 3D viewport. Ports the wbview3d math:
/// screen pixels unproject to a view ray (clickToTerrain screenToVector), the
/// ray intersects the heightfield (m_heightMapRenderObj Cast_Ray), and the hit
/// maps back to a cell with the border-relative world convention shared with
/// <see cref="WbCamera"/>.
/// </summary>
public static class WbPicking
{
    /// <summary>
    /// Ray-march step in feet (quarter cell).
    /// </summary>
    public const float MarchStepFeet = 2.5f;

    /// <summary>
    /// Builds the view ray through a screen pixel by unprojecting the near
    /// and far planes.
    /// </summary>
    /// <param name="pixel">The pixel coordinates (origin top-left).</param>
    /// <param name="viewportSize">The viewport size in pixels.</param>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <returns>The ray origin and normalized direction.</returns>
    public static (Vector3 Origin, Vector3 Direction) ScreenPointToRay(
        Vector2 pixel,
        Vector2 viewportSize,
        Matrix4x4 view,
        Matrix4x4 projection)
    {
        var ndc = new Vector2(
            (2.0f * pixel.X / Math.Max(1.0f, viewportSize.X)) - 1.0f,
            1.0f - (2.0f * pixel.Y / Math.Max(1.0f, viewportSize.Y)));
        if (!Matrix4x4.Invert(view * projection, out var inverse))
        {
            return (Vector3.Zero, -Vector3.UnitZ);
        }

        var near = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 0.0f, 1.0f), inverse);
        var far = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 1.0f, 1.0f), inverse);
        near /= Math.Max(float.Epsilon, near.W);
        far /= Math.Max(float.Epsilon, far.W);
        var origin = new Vector3(near.X, near.Y, near.Z);
        var direction = new Vector3(far.X - near.X, far.Y - near.Y, far.Z - near.Z);
        return direction.LengthSquared() <= float.Epsilon
            ? (origin, -Vector3.UnitZ)
            : (origin, Vector3.Normalize(direction));
    }

    /// <summary>
    /// Marches a ray against the heightfield and refines the first crossing.
    /// Returns null when the ray never enters the map volume.
    /// </summary>
    /// <param name="origin">The ray origin in world feet.</param>
    /// <param name="direction">The normalized ray direction.</param>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="maxDistanceFeet">The maximum march distance.</param>
    /// <returns>The hit position, or null.</returns>
    public static Vector3? IntersectTerrain(Vector3 origin, Vector3 direction, MapTerrainData terrain, float maxDistanceFeet)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var distance = 0.0f;
        var previous = SamplePoint(origin, terrain);
        while (distance < maxDistanceFeet)
        {
            distance += MarchStepFeet;
            var point = origin + (direction * distance);
            var height = SamplePoint(point, terrain);
            if (previous >= 0.0f && height < 0.0f)
            {
                return RefineCrossing(origin, direction, terrain, distance - MarchStepFeet, distance);
            }

            previous = height;
        }

        return null;
    }

    /// <summary>
    /// Intersects a ray with a flat ground plane (the no-heightmap fallback).
    /// Returns null when the ray runs parallel to the plane.
    /// </summary>
    /// <param name="origin">The ray origin.</param>
    /// <param name="direction">The normalized ray direction.</param>
    /// <param name="groundZ">The plane height in feet.</param>
    /// <returns>The hit position, or null.</returns>
    public static Vector3? IntersectGroundPlane(Vector3 origin, Vector3 direction, float groundZ)
    {
        if (Math.Abs(direction.Z) <= float.Epsilon)
        {
            return null;
        }

        var distance = (groundZ - origin.Z) / direction.Z;
        return distance < 0.0f ? null : origin + (direction * distance);
    }

    /// <summary>
    /// Converts border-relative world feet to map cells
    /// (world = (index - border) * cell size).
    /// </summary>
    /// <param name="world">The world position.</param>
    /// <param name="borderSize">The map border in cells.</param>
    /// <param name="mapWidth">The map width in cells.</param>
    /// <param name="mapHeight">The map height in cells.</param>
    /// <returns>The clamped cell coordinates.</returns>
    public static (int X, int Y) WorldToCell(Vector3 world, int borderSize, int mapWidth, int mapHeight)
    {
        return MapCoordinates.WorldToCell(borderSize, world.X, world.Y, mapWidth, mapHeight);
    }

    /// <summary>
    /// Converts map cells to border-relative world feet.
    /// </summary>
    /// <param name="cellX">The cell X.</param>
    /// <param name="cellY">The cell Y.</param>
    /// <param name="borderSize">The map border in cells.</param>
    /// <returns>The world position on the ground plane.</returns>
    public static Vector2 CellToWorld(int cellX, int cellY, int borderSize)
    {
        var world = MapCoordinates.CellToWorld(borderSize, cellX, cellY);
        return new Vector2(world.X, world.Y);
    }

    /// <summary>
    /// Samples terrain height in feet at a world position, for ground-relative
    /// placement: map objects store height above the terrain surface.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="worldX">The world X in feet.</param>
    /// <param name="worldY">The world Y in feet.</param>
    /// <returns>The ground height in feet.</returns>
    public static float GroundHeightFeet(MapTerrainData terrain, float worldX, float worldY)
    {
        return MapCoordinates.SampleGroundHeight(terrain, worldX, worldY);
    }

    /// <summary>
    /// Samples terrain height in feet with bilinear interpolation.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="cellX">The X in cells.</param>
    /// <param name="cellY">The Y in cells.</param>
    /// <returns>The height in feet.</returns>
    public static float SampleHeight(MapTerrainData terrain, float cellX, float cellY)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Heights.Count == 0)
        {
            return 0.0f;
        }

        var x = Math.Clamp(cellX, 0.0f, terrain.Width - 1.001f);
        var y = Math.Clamp(cellY, 0.0f, terrain.Height - 1.001f);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, terrain.Width - 1);
        var y1 = Math.Min(y0 + 1, terrain.Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var top = Lerp(terrain.Heights[(y0 * terrain.Width) + x0], terrain.Heights[(y0 * terrain.Width) + x1], fx);
        var bottom = Lerp(terrain.Heights[(y1 * terrain.Width) + x0], terrain.Heights[(y1 * terrain.Width) + x1], fx);
        return Lerp(top, bottom, fy) * WorldBuilderConstants.Terrain.HeightScale;
    }

    private static float SamplePoint(Vector3 point, MapTerrainData terrain)
    {
        var cellX = (point.X / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize;
        var cellY = (point.Y / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize;
        if (cellX < 0.0f || cellY < 0.0f || cellX > terrain.Width - 1 || cellY > terrain.Height - 1)
        {
            return point.Z >= 0.0f ? 1.0f : -1.0f;
        }

        return point.Z - SampleHeight(terrain, cellX, cellY);
    }

    private static Vector3 RefineCrossing(Vector3 origin, Vector3 direction, MapTerrainData terrain, float near, float far)
    {
        for (var i = 0; i < 10; i++)
        {
            var mid = (near + far) / 2.0f;
            if (SamplePoint(origin + (direction * mid), terrain) >= 0.0f)
            {
                near = mid;
            }
            else
            {
                far = mid;
            }
        }

        return origin + (direction * ((near + far) / 2.0f));
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}
