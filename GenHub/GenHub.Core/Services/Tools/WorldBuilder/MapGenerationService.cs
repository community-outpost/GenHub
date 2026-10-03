using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Generates new map documents procedurally. Ports the QT MapGen Genesis core
/// plus WBMapGenAssets: ring start placement, two-pass Perlin terrain, tanh
/// start flattening, slope limiting, cliff/open-ground texturing, clumped
/// trees, uniform rocks, paired supplies, start-ring roads, and skirmish sides.
/// Per-pass RNG salts keep each toggle independent for a fixed seed.
/// </summary>
public sealed class MapGenerationService(ILogger<MapGenerationService> logger) : IMapGenerationService
{
    private const int BaseRadius = 40;
    private const int StartFalloff = 20;
    private const int MaxHeightDifference = 15;
    private const int MaxGeneratedProps = 1200;
    private const int TimeOfDayAfternoon = 2;

    /// <inheritdoc />
    public OperationResult<WorldBuilderMap> Generate(MapGenSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var players = Math.Clamp(settings.NumPlayers, 2, 8);
            var playableWidth = Math.Max(settings.PlayableWidth, MinimumSize(players));
            var playableHeight = Math.Max(settings.PlayableHeight, MinimumSize(players));
            var border = Math.Max(1, settings.Border);
            var widthLong = (long)playableWidth + (2L * border);
            var heightLong = (long)playableHeight + (2L * border);
            if (widthLong * heightLong > WorldBuilderConstants.Limits.MaxDecompressedSize)
            {
                return OperationResult<WorldBuilderMap>.CreateFailure(
                    $"Map dimensions {playableWidth}x{playableHeight} exceed the {WorldBuilderConstants.Limits.MaxDecompressedSize}-cell generation budget.");
            }

            var width = (int)widthLong;
            var height = (int)heightLong;

            var field = new MapGenField(width, height, (byte)Math.Clamp(settings.BaseHeight, 0, 255));
            var starts = PlaceStarts(field, settings.Seed, players, border, playableWidth, playableHeight);
            GenerateTerrain(field, settings, false, cancellationToken);
            FlattenStarts(field, starts);
            GenerateTerrain(field, settings, true, cancellationToken);
            if (settings.DoCliffs)
            {
                LimitSlopes(field, settings.Seed, cancellationToken);
            }

            var map = BuildDocument(settings, field, starts, border, playableWidth, playableHeight, cancellationToken);
            logger.LogInformation("Generated {Width}x{Height} map with {Players} players from seed {Seed}.", width, height, players, settings.Seed);
            return OperationResult<WorldBuilderMap>.CreateSuccess(map);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return OperationResult<WorldBuilderMap>.CreateFailure($"Map generation failed: {ex.Message}");
        }
    }

    private static int MinimumSize(int players)
    {
        return players switch
        {
            2 => 150,
            4 => 300,
            6 => 400,
            8 => 490,
            _ => players * 75,
        };
    }

    private static List<(int X, int Y)> PlaceStarts(MapGenField field, int seed, int players, int border, int playableWidth, int playableHeight)
    {
        var starts = new List<(int X, int Y)>();
        var halfWidth = playableWidth / 2.0f;
        var halfHeight = playableHeight / 2.0f;
        var radiusX = Math.Max(1.0f, halfWidth - BaseRadius);
        var radiusY = Math.Max(1.0f, halfHeight - BaseRadius);
        var random = new WbRandom((uint)(seed + WorldBuilderConstants.MapGen.SaltStarts));
        var baseAngle = random.NextReal() * 6.283185307f;
        for (var i = 0; i < players; i++)
        {
            var angle = baseAngle + ((6.283185307f * i) / players);
            var x = Math.Clamp(border + (int)(halfWidth + (Math.Cos(angle) * radiusX)), 0, field.Width - 1);
            var y = Math.Clamp(border + (int)(halfHeight + (Math.Sin(angle) * radiusY)), 0, field.Height - 1);
            starts.Add((x, y));
        }

        foreach (var (x, y) in starts)
        {
            for (var j = y - BaseRadius; j <= y + BaseRadius; j++)
            {
                for (var i = x - BaseRadius; i <= x + BaseRadius; i++)
                {
                    var dx = i - x;
                    var dy = j - y;
                    if ((dx * dx) + (dy * dy) <= BaseRadius * BaseRadius)
                    {
                        field.SetBase(i, j);
                    }
                }
            }
        }

        return starts;
    }

    private static void GenerateTerrain(MapGenField field, MapGenSettings settings, bool detailPass, CancellationToken cancellationToken)
    {
        var noise = new WbPerlinNoise(settings.Seed + WorldBuilderConstants.MapGen.SaltTerrain);
        var z = settings.Seed & 0xFF;
        var layers = new[]
        {
            (Height: settings.NoiseHeight, Elevation: settings.NoiseElevation, Jaggedness: settings.NoiseJaggedness, Ruggedness: settings.NoiseRuggedness, Detail: settings.NoiseOctaves, IsDetail: false),
            (Height: -40, Elevation: 1.22f, Jaggedness: 0.014f, Ruggedness: 0.7f, Detail: 2, IsDetail: false),
            (Height: 13, Elevation: 0.79f, Jaggedness: 0.038f, Ruggedness: 0.8f, Detail: 3, IsDetail: true),
        };
        for (var y = 0; y < field.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < field.Width; x++)
            {
                foreach (var layer in layers)
                {
                    if (layer.IsDetail != detailPass)
                    {
                        continue;
                    }

                    noise.Amplitude = layer.Elevation;
                    noise.Frequency = layer.Jaggedness;
                    noise.Persistence = layer.Ruggedness;
                    noise.Octaves = layer.Detail;
                    var delta = (int)(noise.Compute(x, y, z) * layer.Height);
                    field.AddHeight(x, y, delta);
                }
            }
        }
    }

    private static void FlattenStarts(MapGenField field, List<(int X, int Y)> starts)
    {
        foreach (var (x, y) in starts)
        {
            FlattenArea(field, x, y, field.GetHeight(x, y), BaseRadius, StartFalloff);
        }
    }

    private static void FlattenArea(MapGenField field, int centerX, int centerY, byte target, int radius, int falloff)
    {
        if (radius <= 0)
        {
            return;
        }

        if (falloff < 0)
        {
            falloff = 0;
        }

        var outer = radius + falloff;
        const float range = 0.9171523f;
        for (var y = centerY - outer; y <= centerY + outer; y++)
        {
            for (var x = centerX - outer; x <= centerX + outer; x++)
            {
                if (!field.IsValid(x, y))
                {
                    continue;
                }

                var dist = MathF.Sqrt(((x - centerX) * (x - centerX)) + ((y - centerY) * (y - centerY)));
                if (dist <= radius)
                {
                    field.SetHeight(x, y, target);
                    continue;
                }

                if (dist >= outer || falloff == 0)
                {
                    continue;
                }

                var weight = (dist - radius) / falloff;
                weight = (MathF.Tanh(((weight * 2.0f) - 1.0f) * 1.570796327f) + range) / (2.0f * range);
                var blended = target + ((field.GetHeight(x, y) - target) * weight);
                field.SetHeight(x, y, (byte)(blended + 0.5f));
            }
        }
    }

    private static void LimitSlopes(MapGenField field, int seed, CancellationToken cancellationToken)
    {
        var random = new WbRandom((uint)(seed + WorldBuilderConstants.MapGen.SaltSlopes));
        for (var y = 0; y < field.Height - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < field.Width - 1; x++)
            {
                if (field.IsBase(x, y))
                {
                    continue;
                }

                var here = field.GetHeight(x, y);
                CheckNeighbor(field, random, x, y, x + 1, y, here);
                CheckNeighbor(field, random, x, y, x, y + 1, here);
                CheckNeighbor(field, random, x, y, x + 1, y + 1, here);
            }
        }
    }

    private static void CheckNeighbor(MapGenField field, WbRandom random, int x, int y, int nx, int ny, int here)
    {
        if (field.IsBase(nx, ny))
        {
            return;
        }

        var lo = Math.Max(0, here - MaxHeightDifference);
        var hi = Math.Min(255, here + MaxHeightDifference);
        var there = field.GetHeight(nx, ny);
        if (there > hi)
        {
            field.SetHeight(nx, ny, (byte)Math.Max(0, hi - random.NextInt(5)));
            field.SetCliff(x, y);
            field.SetCliff(nx, ny);
        }
        else if (there < lo)
        {
            field.SetHeight(nx, ny, (byte)Math.Min(255, lo + random.NextInt(5)));
            field.SetCliff(x, y);
            field.SetCliff(nx, ny);
        }
    }

    private static WorldBuilderMap BuildDocument(
        MapGenSettings settings,
        MapGenField field,
        List<(int X, int Y)> starts,
        int border,
        int playableWidth,
        int playableHeight,
        CancellationToken cancellationToken)
    {
        var map = new WorldBuilderMap();
        FillTerrain(map, field, border, playableWidth, playableHeight);
        if (settings.DoTextures)
        {
            ApplyTexturing(map, settings, field, cancellationToken);
        }

        map.World.Add(new MapDictValue(WorldBuilderConstants.DictKeys.MapName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: WorldBuilderConstants.MapGen.GeneratedMapName));
        map.World.Add(new MapDictValue(WorldBuilderConstants.DictKeys.Weather, WorldBuilderConstants.DictValueType.Int, IntValue: 0));
        map.World.Add(new MapDictValue(WorldBuilderConstants.DictKeys.CompressionType, WorldBuilderConstants.DictValueType.Int, IntValue: WorldBuilderConstants.Compression.IntentNone));
        AddSides(map, settings);
        AddStarts(map, starts);
        var occupied = new HashSet<(int X, int Y)>(starts);
        var dockGroups = settings.DoSupplies
            ? AddSupplies(map, settings, field, starts, occupied)
            : [];
        if (settings.DoTrees && settings.TreeTemplates.Count > 0)
        {
            ScatterTrees(map, settings, field, occupied, cancellationToken);
        }

        if (settings.DoRocks && settings.RockTemplates.Count > 0)
        {
            ScatterRocks(map, settings, field, occupied, cancellationToken);
        }

        AddRoads(map, settings, starts, dockGroups, cancellationToken);
        FillLighting(map);
        foreach (var side in map.Sides)
        {
            map.Scripts.Add(new ScriptListModel());
        }

        return map;
    }

    private static void FillTerrain(WorldBuilderMap map, MapGenField field, int border, int playableWidth, int playableHeight)
    {
        var terrain = map.Terrain;
        terrain.Width = field.Width;
        terrain.Height = field.Height;
        terrain.BorderSize = border;
        terrain.Boundaries.Add(new MapBoundary(playableWidth, playableHeight));
        terrain.Heights = field.Heights;
        var dataSize = field.Width * field.Height;
        terrain.TileIndices = new short[dataSize];
        terrain.BlendTileIndices = new short[dataSize];
        terrain.ExtraBlendTileIndices = new short[dataSize];
        terrain.CliffInfoIndices = new short[dataSize];
        terrain.CliffState = PackCliffs(field);
        terrain.NumBitmapTiles = 64;
        terrain.NumBlendedTiles = 1;
        terrain.NumCliffInfo = 1;
        terrain.TextureClasses.Add(new MapTextureClass(0, 64, 8, WorldBuilderConstants.MapGen.DefaultGroundTexture));
        terrain.NumEdgeTiles = 0;
    }

    private static byte[] PackCliffs(MapGenField field)
    {
        var stride = (field.Width + 7) / 8;
        var state = new byte[stride * field.Height];
        for (var y = 0; y < field.Height; y++)
        {
            for (var x = 0; x < field.Width; x++)
            {
                if (field.IsCliff(x, y))
                {
                    state[(y * stride) + (x >> 3)] |= (byte)(1 << (x & 0x7));
                }
            }
        }

        return state;
    }

    private static void AddSides(WorldBuilderMap map, MapGenSettings settings)
    {
        AddSide(map, WorldBuilderConstants.Sides.CivilianFaction, WorldBuilderConstants.Sides.CivilianPlayerName);
        if (!settings.DoPlayers)
        {
            return;
        }

        var factions = WorldBuilderConstants.MapGen.SkirmishFactions;
        var names = WorldBuilderConstants.MapGen.SkirmishPlayerNames;
        for (var i = 0; i < factions.Length; i++)
        {
            AddSide(map, factions[i], names[i]);
        }
    }

    private static void AddSide(WorldBuilderMap map, string faction, string playerName)
    {
        var side = new MapSideEntry();
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: playerName));
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerIsHuman, WorldBuilderConstants.DictValueType.Bool, IntValue: 0));
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerDisplayName, WorldBuilderConstants.DictValueType.UnicodeString, StringValue: playerName));
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerFaction, WorldBuilderConstants.DictValueType.AsciiString, StringValue: faction));
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerEnemies, WorldBuilderConstants.DictValueType.AsciiString, StringValue: string.Empty));
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerAllies, WorldBuilderConstants.DictValueType.AsciiString, StringValue: string.Empty));
        map.Sides.Add(side);
    }

    private static void AddStarts(WorldBuilderMap map, List<(int X, int Y)> starts)
    {
        var id = 0;
        foreach (var (x, y) in starts)
        {
            id++;
            var start = new MapObjectEntry
            {
                X = x * WorldBuilderConstants.Terrain.CellSize,
                Y = y * WorldBuilderConstants.Terrain.CellSize,
                Z = 0f,
                Angle = 0f,
                Name = $"Player_{id}_Start",
            };
            start.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.WaypointName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: start.Name));
            start.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.WaypointId, WorldBuilderConstants.DictValueType.Int, IntValue: id));
            start.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.OriginalOwner, WorldBuilderConstants.DictValueType.AsciiString, StringValue: WorldBuilderConstants.Teams.DefaultRootTeam));
            map.Objects.Add(start);
        }
    }

    private static List<List<(int X, int Y)>> AddSupplies(
        WorldBuilderMap map,
        MapGenSettings settings,
        MapGenField field,
        List<(int X, int Y)> starts,
        HashSet<(int X, int Y)> occupied)
    {
        var dockGroups = new List<List<(int X, int Y)>>();
        var random = new WbRandom((uint)(settings.Seed + WorldBuilderConstants.MapGen.SaltSupplies));
        var baseAngle = random.NextReal() * 6.283185307f;
        const int Distance = BaseRadius + 12;
        foreach (var (sx, sy) in starts)
        {
            var docks = new List<(int X, int Y)>();
            dockGroups.Add(docks);
            for (var n = 0; n < 2; n++)
            {
                var angle = baseAngle + ((6.283185307f * n) / 2);
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var tryAngle = angle + (attempt * 0.4f);
                    var cellX = sx + (int)(Math.Cos(tryAngle) * Distance);
                    var cellY = sy + (int)(Math.Sin(tryAngle) * Distance);
                    if (!field.IsValid(cellX, cellY) || field.IsCliff(cellX, cellY) || !occupied.Add((cellX, cellY)))
                    {
                        continue;
                    }

                    var supply = new MapObjectEntry
                    {
                        X = cellX * WorldBuilderConstants.Terrain.CellSize,
                        Y = cellY * WorldBuilderConstants.Terrain.CellSize,
                        Z = 0f,
                        Angle = 0f,
                        Name = settings.SupplyTemplate,
                    };
                    supply.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.OriginalOwner, WorldBuilderConstants.DictValueType.AsciiString, StringValue: WorldBuilderConstants.Teams.DefaultRootTeam));
                    map.Objects.Add(supply);
                    docks.Add((cellX, cellY));
                    break;
                }
            }
        }

        return dockGroups;
    }

    private static void ScatterTrees(WorldBuilderMap map, MapGenSettings settings, MapGenField field, HashSet<(int X, int Y)> occupied, CancellationToken cancellationToken)
    {
        var random = new WbRandom((uint)(settings.Seed + WorldBuilderConstants.MapGen.SaltTrees));
        var cells = field.Width * field.Height;
        var scale = 3.0f;
        if (settings.TreeDensity < 33)
        {
            scale = 1.5f;
        }
        else if (settings.TreeDensity > 66)
        {
            scale = 6.0f;
        }

        var wanted = (int)((cells / 300.0f) * scale);
        var placed = 0;
        var attempts = 0;
        var props = 0;
        var maxAttempts = (wanted * 8) + 64;
        while (placed < wanted && attempts < maxAttempts && props < MaxGeneratedProps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            var cellX = random.NextInt(field.Width);
            var cellY = random.NextInt(field.Height);
            var steps = 1 + random.NextInt(12);
            for (var step = 0; step < steps && placed < wanted && props < MaxGeneratedProps; step++)
            {
                cellX += random.NextRange(-5, 6);
                cellY += random.NextRange(-5, 6);
                if (!CanPlaceProp(field, cellX, cellY, occupied))
                {
                    continue;
                }

                occupied.Add((cellX, cellY));
                AddProp(map, settings.TreeTemplates[random.NextInt(settings.TreeTemplates.Count)], cellX, cellY, random.NextReal() * 6.283185307f);
                placed++;
                props++;
            }
        }
    }

    private static void ScatterRocks(WorldBuilderMap map, MapGenSettings settings, MapGenField field, HashSet<(int X, int Y)> occupied, CancellationToken cancellationToken)
    {
        var random = new WbRandom((uint)(settings.Seed + WorldBuilderConstants.MapGen.SaltRocks));
        var wanted = (field.Width * field.Height) / 2000;
        var placed = 0;
        var attempts = 0;
        var maxAttempts = (wanted * 12) + 64;
        while (placed < wanted && attempts < maxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            var cellX = random.NextInt(field.Width);
            var cellY = random.NextInt(field.Height);
            if (!CanPlaceProp(field, cellX, cellY, occupied))
            {
                continue;
            }

            occupied.Add((cellX, cellY));
            AddProp(map, settings.RockTemplates[random.NextInt(settings.RockTemplates.Count)], cellX, cellY, random.NextReal() * 6.283185307f);
            placed++;
        }
    }

    private static bool CanPlaceProp(MapGenField field, int cellX, int cellY, HashSet<(int X, int Y)> occupied)
    {
        return field.IsValid(cellX, cellY) && !field.IsCliff(cellX, cellY) && !field.IsBase(cellX, cellY) && !occupied.Contains((cellX, cellY));
    }

    private static void AddProp(WorldBuilderMap map, string template, int cellX, int cellY, float angle)
    {
        var prop = new MapObjectEntry
        {
            X = cellX * WorldBuilderConstants.Terrain.CellSize,
            Y = cellY * WorldBuilderConstants.Terrain.CellSize,
            Z = 0f,
            Angle = angle,
            Name = template,
        };
        prop.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.OriginalOwner, WorldBuilderConstants.DictValueType.AsciiString, StringValue: WorldBuilderConstants.Teams.DefaultRootTeam));
        map.Objects.Add(prop);
    }

    private static void ApplyTexturing(WorldBuilderMap map, MapGenSettings settings, MapGenField field, CancellationToken cancellationToken)
    {
        var groundName = string.IsNullOrWhiteSpace(settings.GroundTexture)
            ? WorldBuilderConstants.MapGen.DefaultGroundTexture
            : settings.GroundTexture;
        var cliffName = string.IsNullOrWhiteSpace(settings.CliffTexture)
            ? WorldBuilderConstants.MapGen.DefaultCliffTexture
            : settings.CliffTexture;
        var ground = new MapTextureClass(0, 64, 8, groundName);
        var cliff = new MapTextureClass(0, 64, 8, cliffName);
        MapTerrainTools.EnsureTextureClass(map.Terrain, ground);
        MapTerrainTools.EnsureTextureClass(map.Terrain, cliff);
        var cliffRandom = new WbRandom((uint)(settings.Seed + WorldBuilderConstants.MapGen.SaltCliffs));
        var textureRandom = new WbRandom((uint)(settings.Seed + WorldBuilderConstants.MapGen.SaltTextures));
        for (var y = 0; y < field.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < field.Width; x++)
            {
                MapTerrainTools.PaintTile(map, x, y, 0, PickTexture(field, x, y, ground, cliff, cliffRandom, textureRandom));
            }
        }
    }

    private static MapTextureClass PickTexture(
        MapGenField field,
        int x,
        int y,
        MapTextureClass ground,
        MapTextureClass cliff,
        WbRandom cliffRandom,
        WbRandom textureRandom)
    {
        if (field.IsCliff(x, y))
        {
            return cliff;
        }

        if (MaxNeighborStep(field, x, y) >= WorldBuilderConstants.MapGen.TexturingSteepStep
            && cliffRandom.NextReal() < WorldBuilderConstants.MapGen.TexturingSteepChance)
        {
            return cliff;
        }

        if (HasCliffNeighbor(field, x, y)
            && textureRandom.NextReal() < WorldBuilderConstants.MapGen.TexturingEdgeChance)
        {
            return cliff;
        }

        return ground;
    }

    private static int MaxNeighborStep(MapGenField field, int x, int y)
    {
        var here = field.GetHeight(x, y);
        var max = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var nx = x + dx;
                var ny = y + dy;
                if (!field.IsValid(nx, ny))
                {
                    continue;
                }

                max = Math.Max(max, Math.Abs(field.GetHeight(nx, ny) - here));
            }
        }

        return max;
    }

    private static bool HasCliffNeighbor(MapGenField field, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && field.IsCliff(x + dx, y + dy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AddRoads(
        WorldBuilderMap map,
        MapGenSettings settings,
        List<(int X, int Y)> starts,
        List<List<(int X, int Y)>> dockGroups,
        CancellationToken cancellationToken)
    {
        if (settings.RoadMode == MapGenRoadMode.None || starts.Count < 2)
        {
            return;
        }

        var roadType = string.IsNullOrWhiteSpace(settings.RoadTemplate)
            ? WorldBuilderConstants.MapGen.DefaultRoadTemplate
            : settings.RoadTemplate;
        var waypoints = new List<(int X, int Y)>();
        for (var i = 0; i < starts.Count; i++)
        {
            waypoints.Add(starts[i]);
            if (settings.RoadMode == MapGenRoadMode.Supplies && i < dockGroups.Count)
            {
                waypoints.AddRange(dockGroups[i]);
            }
        }

        var cell = WorldBuilderConstants.Terrain.CellSize;
        for (var i = 0; i < waypoints.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = waypoints[i];
            var b = waypoints[(i + 1) % waypoints.Count];
            MapOverlayTools.AddRoadSegment(map, new RoadSegment(
                roadType,
                a.X * cell,
                a.Y * cell,
                0f,
                b.X * cell,
                b.Y * cell,
                0f));
        }
    }

    private static void FillLighting(WorldBuilderMap map)
    {
        map.Lighting.TimeOfDay = TimeOfDayAfternoon;
        map.Lighting.ShadowColor = 0;
        for (var i = 0; i < WorldBuilderConstants.Limits.TimeOfDayCount; i++)
        {
            var slot = new MapTimeOfDayLighting();
            for (var j = 0; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                slot.TerrainLights.Add(new MapLight { AmbientR = 0.5f, AmbientG = 0.5f, AmbientB = 0.5f, DiffuseR = 1f, DiffuseG = 1f, DiffuseB = 1f, PosZ = -1f });
                slot.ObjectLights.Add(new MapLight { AmbientR = 0.5f, AmbientG = 0.5f, AmbientB = 0.5f, DiffuseR = 1f, DiffuseG = 1f, DiffuseB = 1f, PosZ = -1f });
            }

            map.Lighting.TimesOfDay.Add(slot);
        }
    }
}
