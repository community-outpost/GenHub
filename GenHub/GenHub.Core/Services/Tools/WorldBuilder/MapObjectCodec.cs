using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads and writes SidesList, ObjectsList, and ScriptTeams chunks.
/// Layout mirrors SidesList, MapObject, and team serializers.
/// </summary>
public static class MapObjectCodec
{
    /// <summary>
    /// Writes the SidesList chunk (version 3) with nested player scripts.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="sides">The player sides.</param>
    /// <param name="teams">The skirmish teams.</param>
    /// <param name="scripts">Per-player script lists, aligned with sides.</param>
    public static void WriteSides(
        MapChunkWriter writer,
        IReadOnlyList<MapSideEntry> sides,
        IReadOnlyList<MapTeamEntry> teams,
        IReadOnlyList<ScriptListModel> scripts)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(scripts);
        writer.OpenChunk(WorldBuilderConstants.Chunks.SidesList, WorldBuilderConstants.Versions.SidesList);
        writer.WriteInt(sides.Count);
        foreach (var side in sides)
        {
            writer.WriteDict(side.Properties.Values);
            writer.WriteInt(side.BuildList.Count);
            foreach (var build in side.BuildList)
            {
                WriteBuildEntry(writer, build);
            }
        }

        writer.WriteInt(teams.Count);
        foreach (var team in teams)
        {
            writer.WriteDict(team.Properties.Values);
        }

        MapScriptCodec.WritePlayerScripts(writer, scripts);
        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the SidesList chunk including nested player scripts.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>Sides, teams, and per-player scripts.</returns>
    public static (List<MapSideEntry> Sides, List<MapTeamEntry> Teams, List<ScriptListModel> Scripts) ReadSides(
        MapChunkReader reader,
        MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var sides = new List<MapSideEntry>();
        var teams = new List<MapTeamEntry>();
        var scripts = new List<ScriptListModel>();
        var sideCount = cursor.ReadInt();
        for (var i = 0; i < sideCount && i < WorldBuilderConstants.Limits.MaxPlayerCount; i++)
        {
            var side = new MapSideEntry();
            foreach (var value in cursor.ReadDict().Values)
            {
                side.Properties.Add(value);
            }

            var buildCount = cursor.ReadInt();
            for (var j = 0; j < buildCount; j++)
            {
                side.BuildList.Add(ReadBuildEntry(cursor, node.Version));
            }

            sides.Add(side);
        }

        if (node.Version >= 2)
        {
            var teamCount = cursor.ReadInt();
            for (var i = 0; i < teamCount; i++)
            {
                var team = new MapTeamEntry();
                foreach (var value in cursor.ReadDict().Values)
                {
                    team.Properties.Add(value);
                }

                teams.Add(team);
            }
        }

        var children = reader.ParseChildren(cursor.RemainingBytes());
        var scriptsNode = children.FirstOrDefault(c => c.Label == WorldBuilderConstants.Chunks.PlayerScriptsList);
        if (scriptsNode != null)
        {
            scripts.AddRange(MapScriptCodec.ReadPlayerScripts(reader, scriptsNode));
        }

        return (sides, teams, scripts);
    }

    /// <summary>
    /// Writes the ObjectsList chunk with nested Object entries.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="objects">The map objects.</param>
    public static void WriteObjects(MapChunkWriter writer, IReadOnlyList<MapObjectEntry> objects)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(objects);
        writer.OpenChunk(WorldBuilderConstants.Chunks.ObjectsList, WorldBuilderConstants.Versions.ObjectsList);
        foreach (var mapObject in objects)
        {
            WriteObject(writer, mapObject);
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the ObjectsList chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The map objects.</returns>
    public static List<MapObjectEntry> ReadObjects(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var objects = new List<MapObjectEntry>();
        foreach (var child in reader.ParseChildren(node.Data))
        {
            if (child.Label != WorldBuilderConstants.Chunks.Object)
            {
                continue;
            }

            objects.Add(ReadObject(reader, child));
        }

        return objects;
    }

    /// <summary>
    /// Writes the ScriptTeams chunk: team dictionaries with no count prefix.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="teams">The teams.</param>
    public static void WriteTeams(MapChunkWriter writer, IReadOnlyList<MapTeamEntry> teams)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(teams);
        writer.OpenChunk(WorldBuilderConstants.Chunks.ScriptTeams, WorldBuilderConstants.Versions.ScriptTeams);
        foreach (var team in teams)
        {
            writer.WriteDict(team.Properties.Values);
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the ScriptTeams chunk until end of payload.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The teams.</returns>
    public static List<MapTeamEntry> ReadTeams(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var teams = new List<MapTeamEntry>();
        var cursor = reader.Cursor(node);
        while (!cursor.AtEnd)
        {
            var team = new MapTeamEntry();
            foreach (var value in cursor.ReadDict().Values)
            {
                team.Properties.Add(value);
            }

            teams.Add(team);
        }

        return teams;
    }

    private static void WriteObject(MapChunkWriter writer, MapObjectEntry mapObject)
    {
        writer.OpenChunk(WorldBuilderConstants.Chunks.Object, WorldBuilderConstants.Versions.ObjectsList);
        writer.WriteReal(mapObject.X);
        writer.WriteReal(mapObject.Y);
        writer.WriteReal(mapObject.Z);
        writer.WriteReal(mapObject.Angle);
        writer.WriteInt(mapObject.Flags);
        writer.WriteAscii(mapObject.Name);
        writer.WriteDict(mapObject.Properties.Values);
        writer.CloseChunk();
    }

    private static MapObjectEntry ReadObject(MapChunkReader reader, MapChunkNode node)
    {
        var cursor = reader.Cursor(node);
        var entry = new MapObjectEntry
        {
            X = cursor.ReadReal(),
            Y = cursor.ReadReal(),
            Z = cursor.ReadReal(),
            Angle = cursor.ReadReal(),
            Flags = cursor.ReadInt(),
            Name = cursor.ReadAscii(),
        };
        foreach (var value in cursor.ReadDict().Values)
        {
            entry.Properties.Add(value);
        }

        return entry;
    }

    private static void WriteBuildEntry(MapChunkWriter writer, MapBuildListEntry build)
    {
        writer.WriteAscii(build.BuildingName);
        writer.WriteAscii(build.TemplateName);
        writer.WriteReal(build.X);
        writer.WriteReal(build.Y);
        writer.WriteReal(build.Z);
        writer.WriteReal(build.Angle);
        writer.WriteByte(build.InitiallyBuilt ? (byte)1 : (byte)0);
        writer.WriteInt(build.NumRebuilds);
        writer.WriteAscii(build.Script);
        writer.WriteInt(build.Health);
        writer.WriteByte(build.Whiner ? (byte)1 : (byte)0);
        writer.WriteByte(build.Unsellable ? (byte)1 : (byte)0);
        writer.WriteByte(build.Repairable ? (byte)1 : (byte)0);
    }

    private static MapBuildListEntry ReadBuildEntry(MapChunkCursor cursor, ushort version)
    {
        var entry = new MapBuildListEntry
        {
            BuildingName = cursor.ReadAscii(),
            TemplateName = cursor.ReadAscii(),
            X = cursor.ReadReal(),
            Y = cursor.ReadReal(),
            Z = cursor.ReadReal(),
            Angle = cursor.ReadReal(),
            InitiallyBuilt = cursor.ReadByte() != 0,
            NumRebuilds = cursor.ReadInt(),
        };
        if (version >= 3)
        {
            entry.Script = cursor.ReadAscii();
            entry.Health = cursor.ReadInt();
            entry.Whiner = cursor.ReadByte() != 0;
            entry.Unsellable = cursor.ReadByte() != 0;
            entry.Repairable = cursor.ReadByte() != 0;
        }

        return entry;
    }
}
