using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System.IO;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads and writes triggers, waypoints, lighting, world dictionary,
/// preview pixels, and the tiling probe chunk.
/// </summary>
public static class MapMiscCodec
{
    /// <summary>
    /// Writes the PolygonTriggers chunk (Zero Hour version 4).
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="triggers">The triggers.</param>
    public static void WriteTriggers(MapChunkWriter writer, IReadOnlyList<MapTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(triggers);
        writer.OpenChunk(WorldBuilderConstants.Chunks.PolygonTriggers, WorldBuilderConstants.Versions.Triggers);
        writer.WriteInt(triggers.Count);
        foreach (var trigger in triggers)
        {
            writer.WriteAscii(trigger.Name);
            writer.WriteAscii(trigger.LayerName);
            writer.WriteInt(trigger.Id);
            writer.WriteByte(trigger.IsWaterArea ? (byte)1 : (byte)0);
            writer.WriteByte(trigger.IsRiver ? (byte)1 : (byte)0);
            writer.WriteInt(trigger.RiverStart);
            writer.WriteInt(trigger.Points.Count);
            foreach (var point in trigger.Points)
            {
                writer.WriteInt(point.X);
                writer.WriteInt(point.Y);
                writer.WriteInt(point.Z);
            }
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the PolygonTriggers chunk, accepting versions 2 through 4.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The triggers.</returns>
    public static List<MapTrigger> ReadTriggers(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var count = cursor.ReadInt();
        var triggers = new List<MapTrigger>();
        for (var i = 0; i < count; i++)
        {
            var trigger = new MapTrigger
            {
                Name = cursor.ReadAscii(),
            };
            if (node.Version >= 4)
            {
                trigger.LayerName = cursor.ReadAscii();
            }

            trigger.Id = cursor.ReadInt();
            if (node.Version >= 2)
            {
                trigger.IsWaterArea = cursor.ReadByte() != 0;
            }

            if (node.Version >= 3)
            {
                trigger.IsRiver = cursor.ReadByte() != 0;
                trigger.RiverStart = cursor.ReadInt();
            }

            var points = cursor.ReadInt();
            for (var j = 0; j < points; j++)
            {
                trigger.Points.Add((cursor.ReadInt(), cursor.ReadInt(), cursor.ReadInt()));
            }

            triggers.Add(trigger);
        }

        return triggers;
    }

    /// <summary>
    /// Writes the WaypointsList chunk.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="links">The waypoint links.</param>
    public static void WriteWaypoints(MapChunkWriter writer, IReadOnlyList<MapWaypointLink> links)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(links);
        writer.OpenChunk(WorldBuilderConstants.Chunks.WaypointsList, WorldBuilderConstants.Versions.Waypoints);
        writer.WriteInt(links.Count);
        foreach (var link in links)
        {
            writer.WriteInt(link.Waypoint1);
            writer.WriteInt(link.Waypoint2);
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the WaypointsList chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The waypoint links.</returns>
    public static List<MapWaypointLink> ReadWaypoints(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var count = cursor.ReadInt();
        var links = new List<MapWaypointLink>();
        for (var i = 0; i < count; i++)
        {
            links.Add(new MapWaypointLink(cursor.ReadInt(), cursor.ReadInt()));
        }

        return links;
    }

    /// <summary>
    /// Writes the GlobalLighting chunk (version 3).
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="lighting">The lighting data.</param>
    public static void WriteLighting(MapChunkWriter writer, MapLightingData lighting)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(lighting);
        writer.OpenChunk(WorldBuilderConstants.Chunks.GlobalLighting, WorldBuilderConstants.Versions.Lighting);
        writer.WriteInt(lighting.TimeOfDay);
        foreach (var slot in lighting.TimesOfDay)
        {
            WriteLight(writer, LightAt(slot.TerrainLights, 0));
            WriteLight(writer, LightAt(slot.ObjectLights, 0));
            for (var j = 1; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                WriteLight(writer, LightAt(slot.ObjectLights, j));
            }

            for (var j = 1; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                WriteLight(writer, LightAt(slot.TerrainLights, j));
            }
        }

        writer.WriteInt(lighting.ShadowColor);
        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the GlobalLighting chunk, accepting versions 1 through 3.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The lighting data.</returns>
    public static MapLightingData ReadLighting(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var lighting = new MapLightingData
        {
            TimeOfDay = cursor.ReadInt(),
        };
        for (var i = 0; i < WorldBuilderConstants.Limits.TimeOfDayCount; i++)
        {
            var slot = new MapTimeOfDayLighting();
            slot.TerrainLights.Add(ReadLight(cursor));
            slot.ObjectLights.Add(ReadLight(cursor));
            for (var j = 1; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                slot.ObjectLights.Add(node.Version >= 2 ? ReadLight(cursor) : DefaultLight());
            }

            for (var j = 1; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                slot.TerrainLights.Add(node.Version >= 3 ? ReadLight(cursor) : DefaultLight());
            }

            lighting.TimesOfDay.Add(slot);
        }

        if (!cursor.AtEnd)
        {
            lighting.ShadowColor = cursor.ReadInt();
        }

        return lighting;
    }

    /// <summary>
    /// Writes the WorldInfo chunk.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="world">The world dictionary.</param>
    public static void WriteWorld(MapChunkWriter writer, MapDict world)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);
        writer.OpenChunk(WorldBuilderConstants.Chunks.WorldInfo, WorldBuilderConstants.Versions.WorldInfo);
        writer.WriteDict(world.Values);
        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the WorldInfo chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The world dictionary.</returns>
    public static MapDict ReadWorld(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        return reader.Cursor(node).ReadDict();
    }

    /// <summary>
    /// Writes the legacy MapPreview chunk. Read-only legacy: the real editor never
    /// writes an in-map MapPreview; the real preview is the sidecar .tga.
    /// </summary>
    /// <remarks>
    /// Retained for tests and legacy readers only. WorldBuilderMapService never calls
    /// this from WriteDocument; loaded MapPreview payloads stay in memory and the
    /// sidecar .tga remains the written preview.
    /// </remarks>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="preview">The preview pixels.</param>
    public static void WritePreview(MapChunkWriter writer, MapPreviewData preview)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(preview);
        writer.OpenChunk(WorldBuilderConstants.Chunks.MapPreview, WorldBuilderConstants.Versions.MapPreview);
        writer.WriteInt(preview.Width);
        writer.WriteInt(preview.Height);
        foreach (var pixel in preview.Pixels)
        {
            writer.WriteInt(pixel);
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the legacy MapPreview chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The preview pixels.</returns>
    public static MapPreviewData ReadPreview(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var preview = new MapPreviewData
        {
            Width = cursor.ReadInt(),
            Height = cursor.ReadInt(),
        };
        var max = WorldBuilderConstants.Limits.MaxPreviewDimension;
        if (preview.Width <= 0 || preview.Height <= 0 || preview.Width > max || preview.Height > max
            || (long)preview.Width * preview.Height * 4 > cursor.Remaining)
        {
            throw new InvalidDataException($"Invalid preview dimensions {preview.Width}x{preview.Height}.");
        }

        var pixels = new int[preview.Width * preview.Height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = cursor.ReadInt();
        }

        preview.Pixels = pixels;
        return preview;
    }

    private static MapLight LightAt(List<MapLight> lights, int index)
    {
        return index < lights.Count ? lights[index] : DefaultLight();
    }

    private static void WriteLight(MapChunkWriter writer, MapLight light)
    {
        writer.WriteReal(light.AmbientR);
        writer.WriteReal(light.AmbientG);
        writer.WriteReal(light.AmbientB);
        writer.WriteReal(light.DiffuseR);
        writer.WriteReal(light.DiffuseG);
        writer.WriteReal(light.DiffuseB);
        writer.WriteReal(light.PosX);
        writer.WriteReal(light.PosY);
        writer.WriteReal(light.PosZ);
    }

    private static MapLight ReadLight(MapChunkCursor cursor)
    {
        return new MapLight
        {
            AmbientR = cursor.ReadReal(),
            AmbientG = cursor.ReadReal(),
            AmbientB = cursor.ReadReal(),
            DiffuseR = cursor.ReadReal(),
            DiffuseG = cursor.ReadReal(),
            DiffuseB = cursor.ReadReal(),
            PosX = cursor.ReadReal(),
            PosY = cursor.ReadReal(),
            PosZ = cursor.ReadReal(),
        };
    }

    private static MapLight DefaultLight()
    {
        return new MapLight { PosZ = -1f };
    }
}
