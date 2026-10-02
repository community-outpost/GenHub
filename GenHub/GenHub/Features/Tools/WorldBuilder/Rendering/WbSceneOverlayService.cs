// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Builds 3D overlay line batches: waypoint links in yellow, the playable
/// boundary in the 2D gold, and trigger polygons in red. Endpoints sample the
/// terrain height plus a lift so lines ride above the ground.
/// </summary>
public static class WbSceneOverlayService
{
    private const float LiftFeet = 1.0f;
    private static readonly Vector3 WaypointColor = new(1.0f, 1.0f, 0.0f);
    private static readonly Vector3 BoundaryColor = new(0xE0 / 255.0f, 0xB2 / 255.0f, 0x3C / 255.0f);
    private static readonly Vector3 TriggerColor = new(1.0f, 0.0f, 0.0f);

    /// <summary>
    /// Builds overlay lines for the requested layers.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="layers">The enabled layers.</param>
    /// <returns>The overlay line batch.</returns>
    public static WbOverlayLines Build(WorldBuilderMap map, MapCanvasLayers layers)
    {
        ArgumentNullException.ThrowIfNull(map);
        var vertices = new List<float>();
        if (layers.HasFlag(MapCanvasLayers.Waypoints))
        {
            BuildWaypointLinks(map, vertices);
        }

        if (layers.HasFlag(MapCanvasLayers.Boundary))
        {
            BuildBoundary(map, vertices);
        }

        if (layers.HasFlag(MapCanvasLayers.Triggers))
        {
            BuildTriggers(map, vertices);
        }

        return new WbOverlayLines(vertices.ToArray());
    }

    private static void BuildWaypointLinks(WorldBuilderMap map, List<float> vertices)
    {
        foreach (var link in map.WaypointLinks)
        {
            var first = FindWaypoint(map, link.Waypoint1);
            var second = FindWaypoint(map, link.Waypoint2);
            if (first == null || second == null)
            {
                continue;
            }

            AddLine(vertices, GroundPoint(map, first.X, first.Y), GroundPoint(map, second.X, second.Y), WaypointColor);
        }
    }

    private static MapObjectEntry? FindWaypoint(WorldBuilderMap map, int id)
    {
        return map.Objects.FirstOrDefault(entry => entry.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1) == id);
    }

    private static void BuildBoundary(WorldBuilderMap map, List<float> vertices)
    {
        if (map.Terrain.Boundaries.Count == 0)
        {
            return;
        }

        var boundary = map.Terrain.Boundaries[0];
        var minX = -map.Terrain.BorderSize * WorldBuilderConstants.Terrain.CellSize;
        var minY = -map.Terrain.BorderSize * WorldBuilderConstants.Terrain.CellSize;
        var maxX = (boundary.X - map.Terrain.BorderSize) * WorldBuilderConstants.Terrain.CellSize;
        var maxY = (boundary.Y - map.Terrain.BorderSize) * WorldBuilderConstants.Terrain.CellSize;
        var corners = new Vector3[4]
        {
            GroundPoint(map, minX, minY),
            GroundPoint(map, maxX, minY),
            GroundPoint(map, maxX, maxY),
            GroundPoint(map, minX, maxY),
        };

        for (var i = 0; i < 4; i++)
        {
            AddLine(vertices, corners[i], corners[(i + 1) % 4], BoundaryColor);
        }
    }

    private static void BuildTriggers(WorldBuilderMap map, List<float> vertices)
    {
        map.Triggers
            .Where(trigger => trigger.Points.Count >= 2)
            .SelectMany(trigger => trigger.Points.Select((point, i) => (A: point, B: trigger.Points[(i + 1) % trigger.Points.Count])))
            .ToList()
            .ForEach(segment => AddLine(
                vertices,
                GroundPoint(map, segment.A.X, segment.A.Y),
                GroundPoint(map, segment.B.X, segment.B.Y),
                TriggerColor));
    }

    private static void AddLine(List<float> vertices, Vector3 a, Vector3 b, Vector3 color)
    {
        vertices.Add(a.X);
        vertices.Add(a.Y);
        vertices.Add(a.Z);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(1.0f);
        vertices.Add(b.X);
        vertices.Add(b.Y);
        vertices.Add(b.Z);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(1.0f);
    }

    private static Vector3 GroundPoint(WorldBuilderMap map, float x, float y)
    {
        return new Vector3(x, y, SampleGroundFeet(map, x, y) + LiftFeet);
    }

    private static float SampleGroundFeet(WorldBuilderMap map, float x, float y)
    {
        var terrain = map.Terrain;
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Heights.Count == 0)
        {
            return 0.0f;
        }

        var cellX = Math.Clamp((int)MathF.Round((x / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize), 0, terrain.Width - 1);
        var cellY = Math.Clamp((int)MathF.Round((y / WorldBuilderConstants.Terrain.CellSize) + terrain.BorderSize), 0, terrain.Height - 1);
        var index = (cellY * terrain.Width) + cellX;
        return index < terrain.Heights.Count ? terrain.Heights[index] * WorldBuilderConstants.Terrain.HeightScale : 0.0f;
    }
}
