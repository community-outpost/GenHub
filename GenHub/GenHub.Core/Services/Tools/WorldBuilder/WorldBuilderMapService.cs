// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Loads and saves full Zero Hour .map documents with sidecar files.
/// New documents write chunks in canonical engine order: terrain, world, sides
/// (plus nested scripts), objects, triggers, lighting, waypoints. Loaded documents
/// replay their original top-level order and label ids, re-emitting unknown
/// chunks verbatim for a byte-identical round trip.
/// </summary>
public sealed class WorldBuilderMapService(IMapCompressionService compression, IMapPreviewService previews, ILogger<WorldBuilderMapService> logger) : IWorldBuilderMapService
{
    /// <inheritdoc />
    public async Task<OperationResult<WorldBuilderMap>> LoadAsync(string mapPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileBytes = await File.ReadAllBytesAsync(mapPath, cancellationToken).ConfigureAwait(false);
            var raw = compression.Decompress(fileBytes);
            if (!raw.Success || raw.Data == null)
            {
                return OperationResult<WorldBuilderMap>.CreateFailure(raw.FirstError ?? "Decompression failed.");
            }

            var parsed = MapChunkReader.TryParse(raw.Data);
            if (!parsed.Success || parsed.Data == null)
            {
                return OperationResult<WorldBuilderMap>.CreateFailure(parsed.FirstError ?? "Chunk parsing failed.");
            }

            var map = ParseDocument(parsed.Data);
            map.FilePath = mapPath;
            SeedCompressionIntent(map, compression, fileBytes);
            await LoadCompanionAsync(map, logger, cancellationToken).ConfigureAwait(false);
            return OperationResult<WorldBuilderMap>.CreateSuccess(map);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return OperationResult<WorldBuilderMap>.CreateFailure($"Failed to load map: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveAsync(WorldBuilderMap map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var writer = new MapChunkWriter();
            WriteDocument(writer, map);
            var raw = writer.ToFileBytes();
            var enveloped = ApplyCompression(map, compression, raw);
            if (!enveloped.Success || enveloped.Data == null)
            {
                return OperationResult<bool>.CreateFailure(enveloped.FirstError ?? "Compression failed.");
            }

            var directory = Path.GetDirectoryName(map.FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFile.WriteBytesAsync(map.FilePath, enveloped.Data, cancellationToken).ConfigureAwait(false);
            await SaveCompanionAsync(map, cancellationToken).ConfigureAwait(false);
            await SavePreviewAsync(map, cancellationToken).ConfigureAwait(false);
            map.IsDirty = false;
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return OperationResult<bool>.CreateFailure($"Failed to save map: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public MapSummaryReport Summarize(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var summary = new MapSummaryReport
        {
            MapName = map.World.GetString(WorldBuilderConstants.DictKeys.MapName, Path.GetFileNameWithoutExtension(map.FilePath)),
            Width = map.Terrain.Width,
            Height = map.Terrain.Height,
            SideCount = map.Sides.Count,
            ObjectCount = map.Objects.Count,
            TeamCount = map.Teams.Count,
            TriggerCount = map.Triggers.Count,
            WaypointLinkCount = map.WaypointLinks.Count,
            WaveTrackCount = map.Waves.Count,
        };
        foreach (var list in map.Scripts)
        {
            summary.ScriptsPerPlayer.Add(list.Scripts.Count + list.Groups.Sum(g => g.Scripts.Count));
        }

        return summary;
    }

    private static WorldBuilderMap ParseDocument(MapChunkReader reader)
    {
        var map = new WorldBuilderMap();
        map.LabelTable.AddRange(reader.LabelTable);
        foreach (var node in reader.TopLevel)
        {
            map.ChunkOrder.Add(node.Label);
            ParseNode(reader, map, node);
        }

        NormalizeObjectsOnLoad(map);

        return map;
    }

    private static void ParseNode(MapChunkReader reader, WorldBuilderMap map, MapChunkNode node)
    {
        if (node.Label == WorldBuilderConstants.Chunks.HeightMapData)
        {
            map.Terrain = MapTerrainCodec.ReadHeightMap(reader, node);
        }
        else if (node.Label == WorldBuilderConstants.Chunks.BlendTileData)
        {
            MapTerrainCodec.ReadBlendTile(reader, node, map.Terrain);
        }
        else if (node.Label == WorldBuilderConstants.Chunks.WorldInfo)
        {
            foreach (var value in MapMiscCodec.ReadWorld(reader, node).Values)
            {
                map.World.Add(value);
            }
        }
        else if (node.Label == WorldBuilderConstants.Chunks.SidesList)
        {
            var (sides, teams, scripts) = MapObjectCodec.ReadSides(reader, node);
            map.Sides.AddRange(sides);
            map.Teams.AddRange(teams);
            map.Scripts.AddRange(scripts);
        }
        else if (node.Label == WorldBuilderConstants.Chunks.ObjectsList)
        {
            map.Objects.AddRange(MapObjectCodec.ReadObjects(reader, node));
        }
        else if (node.Label == WorldBuilderConstants.Chunks.PolygonTriggers)
        {
            map.Triggers.AddRange(MapMiscCodec.ReadTriggers(reader, node));
        }
        else if (node.Label == WorldBuilderConstants.Chunks.GlobalLighting)
        {
            map.Lighting = MapMiscCodec.ReadLighting(reader, node);
        }
        else if (node.Label == WorldBuilderConstants.Chunks.MapPreview)
        {
            map.Preview = MapMiscCodec.ReadPreview(reader, node);
        }
        else if (node.Label == WorldBuilderConstants.Chunks.WaypointsList)
        {
            map.WaypointLinks.AddRange(MapMiscCodec.ReadWaypoints(reader, node));
        }
        else if (node.Label == WorldBuilderConstants.Chunks.ScriptTeams)
        {
            map.Teams.AddRange(MapObjectCodec.ReadTeams(reader, node));
        }
        else
        {
            map.UnknownChunks.Add(node);
        }
    }

    private static void WriteDocument(MapChunkWriter writer, WorldBuilderMap map)
    {
        ValidateTerrain(map.Terrain);
        var scripts = PadScripts(map.Scripts, map.Sides.Count);
        if (map.ChunkOrder.Count == 0)
        {
            WriteCanonicalDocument(writer, map, scripts);
        }
        else
        {
            WriteOrderedDocument(writer, map, scripts);
        }
    }

    private static void WriteCanonicalDocument(MapChunkWriter writer, WorldBuilderMap map, List<ScriptListModel> scripts)
    {
        MapTerrainCodec.WriteHeightMap(writer, map.Terrain);
        MapTerrainCodec.WriteBlendTile(writer, map.Terrain);
        MapMiscCodec.WriteWorld(writer, map.World);
        MapObjectCodec.WriteSides(writer, map.Sides, map.Teams, scripts);
        WriteObjectsAbsolute(writer, map);
        MapMiscCodec.WriteTriggers(writer, map.Triggers);
        MapMiscCodec.WriteLighting(writer, map.Lighting);
        MapMiscCodec.WriteWaypoints(writer, map.WaypointLinks);
        WriteUnknownRange(writer, map.UnknownChunks, 0);
    }

    private static void WriteOrderedDocument(MapChunkWriter writer, WorldBuilderMap map, List<ScriptListModel> scripts)
    {
        writer.SeedLabelTable(map.LabelTable);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var unknownIndex = 0;
        foreach (var label in map.ChunkOrder)
        {
            if (IsReplayableKnownChunk(label))
            {
                if (written.Add(label))
                {
                    WriteKnownChunk(writer, map, scripts, label);
                }
            }
            else if (!IsConsumedChunk(label))
            {
                unknownIndex = WriteNextUnknown(writer, map, label, unknownIndex);
            }
        }

        WriteMissingKnownChunks(writer, map, scripts, written);
        WriteUnknownRange(writer, map.UnknownChunks, unknownIndex);
    }

    private static bool IsReplayableKnownChunk(string label)
    {
        return label == WorldBuilderConstants.Chunks.HeightMapData
            || label == WorldBuilderConstants.Chunks.BlendTileData
            || label == WorldBuilderConstants.Chunks.WorldInfo
            || label == WorldBuilderConstants.Chunks.SidesList
            || label == WorldBuilderConstants.Chunks.ObjectsList
            || label == WorldBuilderConstants.Chunks.PolygonTriggers
            || label == WorldBuilderConstants.Chunks.GlobalLighting
            || label == WorldBuilderConstants.Chunks.WaypointsList;
    }

    private static bool IsConsumedChunk(string label)
    {
        // MapPreview is legacy read-only (the real preview is the sidecar .tga) and
        // top-level ScriptTeams is merged into SidesList on write, so neither is replayed.
        return label == WorldBuilderConstants.Chunks.MapPreview
            || label == WorldBuilderConstants.Chunks.ScriptTeams;
    }

    private static void WriteKnownChunk(MapChunkWriter writer, WorldBuilderMap map, List<ScriptListModel> scripts, string label)
    {
        switch (label)
        {
            case WorldBuilderConstants.Chunks.HeightMapData:
                MapTerrainCodec.WriteHeightMap(writer, map.Terrain);
                break;
            case WorldBuilderConstants.Chunks.BlendTileData:
                MapTerrainCodec.WriteBlendTile(writer, map.Terrain);
                break;
            case WorldBuilderConstants.Chunks.WorldInfo:
                MapMiscCodec.WriteWorld(writer, map.World);
                break;
            case WorldBuilderConstants.Chunks.SidesList:
                MapObjectCodec.WriteSides(writer, map.Sides, map.Teams, scripts);
                break;
            case WorldBuilderConstants.Chunks.ObjectsList:
                WriteObjectsAbsolute(writer, map);
                break;
            case WorldBuilderConstants.Chunks.PolygonTriggers:
                MapMiscCodec.WriteTriggers(writer, map.Triggers);
                break;
            case WorldBuilderConstants.Chunks.GlobalLighting:
                MapMiscCodec.WriteLighting(writer, map.Lighting);
                break;
            case WorldBuilderConstants.Chunks.WaypointsList:
                MapMiscCodec.WriteWaypoints(writer, map.WaypointLinks);
                break;
            default:
                // Unknown labels are written by the unknown-chunk pass below.
                break;
        }
    }

    private static int WriteNextUnknown(MapChunkWriter writer, WorldBuilderMap map, string label, int start)
    {
        for (var i = start; i < map.UnknownChunks.Count; i++)
        {
            if (string.Equals(map.UnknownChunks[i].Label, label, StringComparison.Ordinal))
            {
                WriteUnknownChunk(writer, map.UnknownChunks[i]);
                return i + 1;
            }
        }

        return start;
    }

    private static void WriteUnknownRange(MapChunkWriter writer, List<MapChunkNode> unknowns, int start)
    {
        for (var i = start; i < unknowns.Count; i++)
        {
            WriteUnknownChunk(writer, unknowns[i]);
        }
    }

    private static void WriteUnknownChunk(MapChunkWriter writer, MapChunkNode node)
    {
        writer.OpenChunk(node.Label, node.Version);
        writer.WriteBytes(node.Data);
        writer.CloseChunk();
    }

    private static void WriteMissingKnownChunks(
        MapChunkWriter writer,
        WorldBuilderMap map,
        List<ScriptListModel> scripts,
        HashSet<string> written)
    {
        foreach (var label in CanonicalChunkOrder().Where(written.Add))
        {
            WriteKnownChunk(writer, map, scripts, label);
        }
    }

    private static IReadOnlyList<string> CanonicalChunkOrder()
    {
        return
        [
            WorldBuilderConstants.Chunks.HeightMapData,
            WorldBuilderConstants.Chunks.BlendTileData,
            WorldBuilderConstants.Chunks.WorldInfo,
            WorldBuilderConstants.Chunks.SidesList,
            WorldBuilderConstants.Chunks.ObjectsList,
            WorldBuilderConstants.Chunks.PolygonTriggers,
            WorldBuilderConstants.Chunks.GlobalLighting,
            WorldBuilderConstants.Chunks.WaypointsList,
        ];
    }

    private static void ValidateTerrain(MapTerrainData terrain)
    {
        if (terrain.Width <= 0 || terrain.Height <= 0)
        {
            throw new InvalidDataException("Terrain dimensions must be positive.");
        }

        var dataSize = terrain.Width * terrain.Height;
        if (terrain.Heights.Count != dataSize
            || terrain.TileIndices.Count != dataSize
            || terrain.BlendTileIndices.Count != dataSize
            || terrain.ExtraBlendTileIndices.Count != dataSize
            || terrain.CliffInfoIndices.Count != dataSize)
        {
            throw new InvalidDataException("Terrain arrays do not match dimensions.");
        }

        var flipWidth = ((terrain.Width + 7) / 8) * terrain.Height;
        if (terrain.CliffState.Count != flipWidth)
        {
            throw new InvalidDataException("Cliff state size does not match dimensions.");
        }
    }

    private static List<ScriptListModel> PadScripts(List<ScriptListModel> scripts, int sides)
    {
        var padded = new List<ScriptListModel>(scripts);
        while (padded.Count < sides)
        {
            padded.Add(new ScriptListModel());
        }

        return padded;
    }

    private static OperationResult<byte[]> ApplyCompression(WorldBuilderMap map, IMapCompressionService compressor, byte[] raw)
    {
        var intent = map.World.GetInt(WorldBuilderConstants.DictKeys.CompressionType, WorldBuilderConstants.Compression.IntentNone);
        if (intent == WorldBuilderConstants.Compression.IntentNone)
        {
            return OperationResult<byte[]>.CreateSuccess(raw);
        }

        var level = intent >= WorldBuilderConstants.Compression.IntentZLibBase
            && intent <= WorldBuilderConstants.Compression.IntentZLibMax
            ? intent - WorldBuilderConstants.Compression.IntentZLibBase + 1
            : WorldBuilderConstants.Compression.DefaultZLibLevel;
        var compressed = compressor.CompressZLib(raw, level);
        if (!compressed.Success || compressed.Data == null)
        {
            return OperationResult<byte[]>.CreateFailure(compressed.FirstError ?? "Compression failed.");
        }

        // GenHub has no RefPack encoder, so RefPack (and legacy Nox/BTree/Huff)
        // intents fall back to ZLib; record the envelope actually written.
        map.World.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.CompressionType,
            WorldBuilderConstants.DictValueType.Int,
            IntValue: WorldBuilderConstants.Compression.IntentZLibBase + level - 1));
        return compressed;
    }

    private static void SeedCompressionIntent(WorldBuilderMap map, IMapCompressionService compressor, byte[] fileBytes)
    {
        if (map.World.Find(WorldBuilderConstants.DictKeys.CompressionType) != null)
        {
            return;
        }

        var intent = compressor.GetCompressionIntent(fileBytes);
        if (intent is { Success: true, Data: int detected }
            && detected != WorldBuilderConstants.Compression.IntentNone)
        {
            map.World.Set(new MapDictValue(
                WorldBuilderConstants.DictKeys.CompressionType,
                WorldBuilderConstants.DictValueType.Int,
                IntValue: detected));
        }
    }

    private static async Task SaveCompanionAsync(WorldBuilderMap map, CancellationToken cancellationToken)
    {
        var wakPath = Path.ChangeExtension(map.FilePath, WorldBuilderConstants.FileExtensions.WaveTracks);
        var bytes = WakCodec.Encode(map.Waves);
        await AtomicFile.WriteBytesAsync(wakPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task LoadCompanionAsync(WorldBuilderMap map, ILogger<WorldBuilderMapService> companionLogger, CancellationToken cancellationToken)
    {
        var wakPath = Path.ChangeExtension(map.FilePath, WorldBuilderConstants.FileExtensions.WaveTracks);
        if (File.Exists(wakPath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(wakPath, cancellationToken).ConfigureAwait(false);
                var tracks = WakCodec.Decode(bytes);
                if (tracks.Success && tracks.Data != null)
                {
                    map.Waves.AddRange(tracks.Data);
                }
            }
            catch (IOException ex)
            {
                companionLogger.LogDebug(ex, "Optional companion {WakPath} skipped.", wakPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                companionLogger.LogDebug(ex, "Optional companion {WakPath} skipped.", wakPath);
            }
        }
    }

    private static void NormalizeObjectsOnLoad(WorldBuilderMap map)
    {
        if (map.Terrain == null || map.Terrain.Heights.Count == 0)
        {
            return;
        }

        foreach (var obj in map.Objects)
        {
            var groundZ = MapCoordinates.SampleGroundHeight(map.Terrain, obj.X, obj.Y);
            obj.Z -= groundZ;
        }
    }

    private static void WriteObjectsAbsolute(MapChunkWriter writer, WorldBuilderMap map)
    {
        if (map.Terrain == null || map.Terrain.Heights.Count == 0)
        {
            MapObjectCodec.WriteObjects(writer, map.Objects);
            return;
        }

        var normalized = new List<MapObjectEntry>(map.Objects.Count);
        foreach (var obj in map.Objects)
        {
            var groundZ = MapCoordinates.SampleGroundHeight(map.Terrain, obj.X, obj.Y);
            var copy = new MapObjectEntry
            {
                X = obj.X,
                Y = obj.Y,
                Z = obj.Z + groundZ,
                Angle = obj.Angle,
                Flags = obj.Flags,
                Name = obj.Name,
            };
            foreach (var prop in obj.Properties.Values)
            {
                copy.Properties.Add(prop);
            }

            normalized.Add(copy);
        }

        MapObjectCodec.WriteObjects(writer, normalized);
    }

    private async Task SavePreviewAsync(WorldBuilderMap map, CancellationToken cancellationToken)
    {
        var preview = previews.BuildPreview(map);
        var result = await previews.WriteTgaAsync(map.FilePath, preview, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            logger.LogWarning("Saved {MapPath} but the sidecar preview failed: {Error}.", map.FilePath, result.FirstError);
        }
    }
}
