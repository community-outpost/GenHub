using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Object, waypoint, road, bridge, and trigger edits for the map canvas.
/// </summary>
public static class MapOverlayTools
{
    /// <summary>
    /// Places an object, snapping Z to the terrain height.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The object template name.</param>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <returns>The placed object.</returns>
    public static MapObjectEntry PlaceObject(WorldBuilderMap map, string name, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var obj = new MapObjectEntry
        {
            X = x,
            Y = y,
            Z = 0,
            Name = UniqueName(map, name),
        };
        map.Objects.Add(obj);
        return obj;
    }

    /// <summary>
    /// Deletes the first object with the given name.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The object name.</param>
    /// <returns>True when an object was removed.</returns>
    public static bool DeleteObject(WorldBuilderMap map, string name)
    {
        ArgumentNullException.ThrowIfNull(map);
        var obj = FindObject(map, name);
        if (obj == null)
        {
            return false;
        }

        return map.Objects.Remove(obj);
    }

    /// <summary>
    /// Deletes a specific object entry by reference. Prefer this over the
    /// name lookup when the entry is already in hand: names are not unique
    /// (scattered props share template names).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="entry">The entry to remove.</param>
    /// <returns>True when the entry was removed.</returns>
    public static bool DeleteObject(WorldBuilderMap map, MapObjectEntry entry)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(entry);
        return map.Objects.Remove(entry);
    }

    /// <summary>
    /// Moves an object, storing a zero ground offset (height above the terrain surface is resolved at render time).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The object name.</param>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <returns>True when the object was found.</returns>
    public static bool MoveObject(WorldBuilderMap map, string name, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var obj = FindObject(map, name);
        if (obj == null)
        {
            return false;
        }

        obj.X = x;
        obj.Y = y;
        obj.Z = 0;
        return true;
    }

    /// <summary>
    /// Finds the first object with the given name.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The object name.</param>
    /// <returns>The object, or null.</returns>
    public static MapObjectEntry? FindObject(WorldBuilderMap map, string name)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Objects.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a waypoint object with the next free id.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <returns>The waypoint object.</returns>
    public static MapObjectEntry AddWaypoint(WorldBuilderMap map, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var id = NextWaypointId(map);
        var waypoint = PlaceObject(map, $"Waypoint {id}", x, y);
        waypoint.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.WaypointId,
            WorldBuilderConstants.DictValueType.Int,
            IntValue: id));
        waypoint.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.WaypointName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: waypoint.Name));
        return waypoint;
    }

    /// <summary>
    /// Links two waypoints by id.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="first">The first waypoint id.</param>
    /// <param name="second">The second waypoint id.</param>
    /// <returns>True when the link was added.</returns>
    public static bool LinkWaypoints(WorldBuilderMap map, int first, int second)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (first == second || !HasWaypoint(map, first) || !HasWaypoint(map, second))
        {
            return false;
        }

        if (map.WaypointLinks.Any(l => Matches(l, first, second)))
        {
            return false;
        }

        map.WaypointLinks.Add(new MapWaypointLink(first, second));
        return true;
    }

    /// <summary>
    /// Removes the link between two waypoints.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="first">The first waypoint id.</param>
    /// <param name="second">The second waypoint id.</param>
    /// <returns>True when a link was removed.</returns>
    public static bool UnlinkWaypoints(WorldBuilderMap map, int first, int second)
    {
        ArgumentNullException.ThrowIfNull(map);
        var link = map.WaypointLinks.FirstOrDefault(l => Matches(l, first, second));
        if (link == null)
        {
            return false;
        }

        return map.WaypointLinks.Remove(link);
    }

    /// <summary>
    /// Adds a paired road segment to the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="segment">The road segment to add.</param>
    /// <returns>The start and end object pair.</returns>
    public static (MapObjectEntry Start, MapObjectEntry End) AddRoadSegment(
        WorldBuilderMap map,
        RoadSegment segment)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrEmpty(segment.RoadType);

        var flags1 = WorldBuilderConstants.ObjectFlags.RoadPoint1;
        if (segment.IsAngled)
        {
            flags1 |= WorldBuilderConstants.ObjectFlags.RoadCornerAngled;
        }

        if (segment.IsTight)
        {
            flags1 |= WorldBuilderConstants.ObjectFlags.RoadCornerTight;
        }

        var p1 = new MapObjectEntry
        {
            Name = segment.RoadType,
            X = segment.X1,
            Y = segment.Y1,
            Z = segment.Z1,
            Flags = flags1,
        };

        var p2 = new MapObjectEntry
        {
            Name = segment.RoadType,
            X = segment.X2,
            Y = segment.Y2,
            Z = segment.Z2,
            Flags = WorldBuilderConstants.ObjectFlags.RoadPoint2,
        };

        map.Objects.Add(p1);
        map.Objects.Add(p2);
        return (p1, p2);
    }

    /// <summary>
    /// Gets all paired road segments in the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <returns>The list of road segments.</returns>
    public static List<RoadSegment> GetRoadSegments(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var segments = new List<RoadSegment>();
        var i = 0;
        while (i < map.Objects.Count - 1)
        {
            var o1 = map.Objects[i];
            if ((o1.Flags & WorldBuilderConstants.ObjectFlags.RoadPoint1) != 0)
            {
                var o2 = map.Objects[i + 1];
                if ((o2.Flags & WorldBuilderConstants.ObjectFlags.RoadPoint2) != 0)
                {
                    var isAngled = (o1.Flags & WorldBuilderConstants.ObjectFlags.RoadCornerAngled) != 0;
                    var isTight = (o1.Flags & WorldBuilderConstants.ObjectFlags.RoadCornerTight) != 0;
                    segments.Add(new RoadSegment(o1.Name, o1.X, o1.Y, o1.Z, o2.X, o2.Y, o2.Z, isAngled, isTight));
                    i += 2;
                    continue;
                }
            }

            i++;
        }

        return segments;
    }

    /// <summary>
    /// Deletes the road segment at the specified index.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="index">The segment index.</param>
    /// <returns>True when a segment was removed.</returns>
    public static bool DeleteRoadSegment(WorldBuilderMap map, int index)
    {
        ArgumentNullException.ThrowIfNull(map);
        var current = 0;
        var i = 0;
        while (i < map.Objects.Count - 1)
        {
            var o1 = map.Objects[i];
            if ((o1.Flags & WorldBuilderConstants.ObjectFlags.RoadPoint1) != 0)
            {
                var o2 = map.Objects[i + 1];
                if ((o2.Flags & WorldBuilderConstants.ObjectFlags.RoadPoint2) != 0)
                {
                    if (current == index)
                    {
                        map.Objects.RemoveAt(i + 1);
                        map.Objects.RemoveAt(i);
                        return true;
                    }

                    current++;
                    i += 2;
                    continue;
                }
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// Adds a paired bridge to the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="template">The bridge object template name.</param>
    /// <param name="x1">World X start.</param>
    /// <param name="y1">World Y start.</param>
    /// <param name="x2">World X end.</param>
    /// <param name="y2">World Y end.</param>
    /// <returns>The start and end bridge pair.</returns>
    public static (MapObjectEntry Start, MapObjectEntry End) AddBridge(
        WorldBuilderMap map,
        string template,
        float x1,
        float y1,
        float x2,
        float y2)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrEmpty(template);

        var p1 = new MapObjectEntry
        {
            Name = template,
            X = x1,
            Y = y1,
            Z = 0,
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint1,
        };

        var p2 = new MapObjectEntry
        {
            Name = template,
            X = x2,
            Y = y2,
            Z = 0,
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint2,
        };

        map.Objects.Add(p1);
        map.Objects.Add(p2);
        return (p1, p2);
    }

    /// <summary>
    /// Gets all bridges in the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <returns>The list of bridge segments.</returns>
    public static List<BridgeSegment> GetBridges(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var bridges = new List<BridgeSegment>();
        var i = 0;
        while (i < map.Objects.Count - 1)
        {
            var o1 = map.Objects[i];
            if ((o1.Flags & WorldBuilderConstants.ObjectFlags.BridgePoint1) != 0)
            {
                var o2 = map.Objects[i + 1];
                if ((o2.Flags & WorldBuilderConstants.ObjectFlags.BridgePoint2) != 0)
                {
                    bridges.Add(new BridgeSegment(o1.Name, o1.X, o1.Y, o1.Z, o2.X, o2.Y, o2.Z));
                    i += 2;
                    continue;
                }
            }

            i++;
        }

        return bridges;
    }

    /// <summary>
    /// Deletes the bridge at the specified index.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="index">The bridge index.</param>
    /// <returns>True when a bridge was removed.</returns>
    public static bool DeleteBridge(WorldBuilderMap map, int index)
    {
        ArgumentNullException.ThrowIfNull(map);
        var current = 0;
        var i = 0;
        while (i < map.Objects.Count - 1)
        {
            var o1 = map.Objects[i];
            if ((o1.Flags & WorldBuilderConstants.ObjectFlags.BridgePoint1) != 0)
            {
                var o2 = map.Objects[i + 1];
                if ((o2.Flags & WorldBuilderConstants.ObjectFlags.BridgePoint2) != 0)
                {
                    if (current == index)
                    {
                        map.Objects.RemoveAt(i + 1);
                        map.Objects.RemoveAt(i);
                        return true;
                    }

                    current++;
                    i += 2;
                    continue;
                }
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// Adds an area trigger polygon.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The trigger name.</param>
    /// <param name="points">World-space polygon points in feet (ICoord3D).</param>
    /// <returns>The trigger.</returns>
    public static MapTrigger AddTrigger(WorldBuilderMap map, string name, IReadOnlyList<(int X, int Y, int Z)> points)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(points);

        var trigger = new MapTrigger { Name = name, Id = NextTriggerId(map) };
        trigger.Points.AddRange(points);
        map.Triggers.Add(trigger);
        return trigger;
    }

    /// <summary>
    /// Adds a water or river area polygon.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="name">The area name.</param>
    /// <param name="points">World-space polygon points in feet (ICoord3D).</param>
    /// <param name="isRiver">True when the polygon represents a river.</param>
    /// <returns>The water area trigger.</returns>
    public static MapTrigger AddWaterArea(WorldBuilderMap map, string name, IReadOnlyList<(int X, int Y, int Z)> points, bool isRiver = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(points);

        var trigger = new MapTrigger
        {
            Name = name,
            Id = NextTriggerId(map),
            IsWaterArea = true,
            IsRiver = isRiver,
        };
        trigger.Points.AddRange(points);
        map.Triggers.Add(trigger);
        return trigger;
    }

    /// <summary>
    /// Deletes the trigger with the given id.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="id">The trigger id.</param>
    /// <returns>True when a trigger was removed.</returns>
    public static bool DeleteTrigger(WorldBuilderMap map, int id)
    {
        ArgumentNullException.ThrowIfNull(map);
        var trigger = map.Triggers.FirstOrDefault(t => t.Id == id);
        if (trigger == null)
        {
            return false;
        }

        return map.Triggers.Remove(trigger);
    }

    /// <summary>
    /// Moves one trigger polygon vertex in world feet.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="id">The trigger id.</param>
    /// <param name="vertex">The vertex index.</param>
    /// <param name="x">World X in feet.</param>
    /// <param name="y">World Y in feet.</param>
    /// <returns>True when the vertex was moved.</returns>
    public static bool MoveTriggerPoint(WorldBuilderMap map, int id, int vertex, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var trigger = map.Triggers.FirstOrDefault(t => t.Id == id);
        if (trigger == null || vertex < 0 || vertex >= trigger.Points.Count)
        {
            return false;
        }

        trigger.Points[vertex] = (x, y, trigger.Points[vertex].Z);
        return true;
    }

    /// <summary>
    /// Inserts a trigger polygon vertex in world feet.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="id">The trigger id.</param>
    /// <param name="vertex">The insertion index, clamped to the point list.</param>
    /// <param name="x">World X in feet.</param>
    /// <param name="y">World Y in feet.</param>
    /// <returns>True when the vertex was inserted.</returns>
    public static bool InsertTriggerPoint(WorldBuilderMap map, int id, int vertex, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var trigger = map.Triggers.FirstOrDefault(t => t.Id == id);
        if (trigger == null)
        {
            return false;
        }

        trigger.Points.Insert(Math.Clamp(vertex, 0, trigger.Points.Count), (x, y, 0));
        return true;
    }

    /// <summary>
    /// Deletes a trigger polygon vertex, keeping at least the trigger itself.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="id">The trigger id.</param>
    /// <param name="vertex">The vertex index.</param>
    /// <returns>True when the vertex was deleted.</returns>
    public static bool DeleteTriggerPoint(WorldBuilderMap map, int id, int vertex)
    {
        ArgumentNullException.ThrowIfNull(map);
        var trigger = map.Triggers.FirstOrDefault(t => t.Id == id);
        if (trigger == null || vertex < 0 || vertex >= trigger.Points.Count)
        {
            return false;
        }

        trigger.Points.RemoveAt(vertex);
        return true;
    }

    /// <summary>
    /// Places a line of fence objects with even spacing (FenceTool). Posts
    /// snap Z to the terrain; the yaw of each post faces along the line.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="templateName">The fence post template name.</param>
    /// <param name="x1">Start world X in feet.</param>
    /// <param name="y1">Start world Y in feet.</param>
    /// <param name="x2">End world X in feet.</param>
    /// <param name="y2">End world Y in feet.</param>
    /// <param name="spacingFeet">Post spacing in feet.</param>
    /// <returns>The placed posts.</returns>
    public static IReadOnlyList<MapObjectEntry> ApplyFence(
        WorldBuilderMap map,
        string templateName,
        float x1,
        float y1,
        float x2,
        float y2,
        float spacingFeet)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        var spacing = Math.Max(spacingFeet, float.Epsilon);
        var steps = Math.Max(1, (int)Math.Round(length / spacing));
        var yaw = MathF.Atan2(dy, dx);
        var posts = new List<MapObjectEntry>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var post = PlaceObject(map, templateName, x1 + (dx * t), y1 + (dy * t));
            post.Angle = yaw;
            posts.Add(post);
        }

        return posts;
    }

    /// <summary>
    /// Snaps a world point to a nearby road or bridge endpoint so new segments
    /// join the existing network. Returns the original point when nothing is
    /// within tolerance.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="x">World X in feet.</param>
    /// <param name="y">World Y in feet.</param>
    /// <param name="toleranceFeet">The snap radius in feet.</param>
    /// <returns>The snapped point.</returns>
    public static (float X, float Y) SnapRoadPoint(WorldBuilderMap map, float x, float y, float toleranceFeet)
    {
        ArgumentNullException.ThrowIfNull(map);
        var best = (X: x, Y: y);
        var bestDistance = Math.Max(toleranceFeet, 0.0f);
        foreach (var segment in GetRoadSegments(map))
        {
            best = NearestEndpoint((segment.X1, segment.Y1, segment.X2, segment.Y2), x, y, best, ref bestDistance);
        }

        foreach (var bridge in GetBridges(map))
        {
            best = NearestEndpoint((bridge.X1, bridge.Y1, bridge.X2, bridge.Y2), x, y, best, ref bestDistance);
        }

        return best;
    }

    private static (float X, float Y) NearestEndpoint(
        (float X1, float Y1, float X2, float Y2) endpoints,
        float x,
        float y,
        (float X, float Y) best,
        ref float bestDistance)
    {
        var first = MathF.Sqrt(((endpoints.X1 - x) * (endpoints.X1 - x)) + ((endpoints.Y1 - y) * (endpoints.Y1 - y)));
        if (first <= bestDistance)
        {
            bestDistance = first;
            best = (endpoints.X1, endpoints.Y1);
        }

        var second = MathF.Sqrt(((endpoints.X2 - x) * (endpoints.X2 - x)) + ((endpoints.Y2 - y) * (endpoints.Y2 - y)));
        if (second <= bestDistance)
        {
            bestDistance = second;
            best = (endpoints.X2, endpoints.Y2);
        }

        return best;
    }

    private static string UniqueName(WorldBuilderMap map, string name)
    {
        if (FindObject(map, name) == null)
        {
            return name;
        }

        var suffix = 2;
        while (FindObject(map, $"{name} {suffix}") != null)
        {
            suffix++;
        }

        return $"{name} {suffix}";
    }

    private static int NextWaypointId(WorldBuilderMap map)
    {
        var max = map.Objects
            .Where(o => o.Properties.Find(WorldBuilderConstants.DictKeys.WaypointId) != null)
            .Select(o => o.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, 0))
            .DefaultIfEmpty(0)
            .Max();

        return max + 1;
    }

    private static bool HasWaypoint(WorldBuilderMap map, int id)
    {
        return map.Objects.Any(o =>
            o.Properties.Find(WorldBuilderConstants.DictKeys.WaypointId) != null
            && o.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, 0) == id);
    }

    private static bool Matches(MapWaypointLink link, int first, int second)
    {
        return (link.Waypoint1 == first && link.Waypoint2 == second)
            || (link.Waypoint1 == second && link.Waypoint2 == first);
    }

    private static int NextTriggerId(WorldBuilderMap map)
    {
        return map.Triggers.Count == 0 ? 1 : map.Triggers.Max(t => t.Id) + 1;
    }
}
