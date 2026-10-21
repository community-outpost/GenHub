using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Deep snapshot of the editable document state for undo and redo.
/// </summary>
public sealed class MapMemento
{
    private MapMemento()
    {
    }

    private int TerrainWidth { get; init; }

    private int TerrainHeight { get; init; }

    private int BorderSize { get; init; }

    private List<MapBoundary> Boundaries { get; init; } = [];

    private int NumBitmapTiles { get; init; }

    private int NumBlendedTiles { get; init; }

    private int NumCliffInfo { get; init; }

    private int NumEdgeTiles { get; init; }

    private List<MapTextureClass> TextureClasses { get; init; } = [];

    private List<MapEdgeTextureClass> EdgeTextureClasses { get; init; } = [];

    private List<MapBlendTile> BlendTiles { get; init; } = [];

    private List<MapCliffInfo> CliffInfos { get; init; } = [];

    private byte[] heights = [];

    private short[] tileIndices = [];

    private short[] blendTileIndices = [];

    private short[] extraBlendTileIndices = [];

    private short[] cliffInfoIndices = [];

    private byte[] cliffState = [];

    private List<MapDictValue> World { get; init; } = [];

    private List<MapSideEntry> Sides { get; init; } = [];

    private List<MapTeamEntry> Teams { get; init; } = [];

    private List<MapObjectEntry> Objects { get; init; } = [];

    private List<MapTrigger> Triggers { get; init; } = [];

    private List<ScriptListModel> Scripts { get; init; } = [];

    private List<MapWaypointLink> WaypointLinks { get; init; } = [];

    private MapLightingData Lighting { get; init; } = new();

    private bool IsDirty { get; init; }

    /// <summary>
    /// Captures the editable state of a map document.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <returns>The snapshot.</returns>
    public static MapMemento Capture(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new MapMemento
        {
            TerrainWidth = map.Terrain.Width,
            TerrainHeight = map.Terrain.Height,
            BorderSize = map.Terrain.BorderSize,
            Boundaries = [.. map.Terrain.Boundaries],
            NumBitmapTiles = map.Terrain.NumBitmapTiles,
            NumBlendedTiles = map.Terrain.NumBlendedTiles,
            NumCliffInfo = map.Terrain.NumCliffInfo,
            NumEdgeTiles = map.Terrain.NumEdgeTiles,
            TextureClasses = map.Terrain.TextureClasses.Select(c => c with { }).ToList(),
            EdgeTextureClasses = map.Terrain.EdgeTextureClasses.Select(c => c with { }).ToList(),
            BlendTiles = map.Terrain.BlendTiles.Select(b => b with { }).ToList(),
            CliffInfos = map.Terrain.CliffInfos.Select(c => c with { U = c.U.ToArray() }).ToList(),
            heights = map.Terrain.Heights.ToArray(),
            tileIndices = map.Terrain.TileIndices.ToArray(),
            blendTileIndices = map.Terrain.BlendTileIndices.ToArray(),
            extraBlendTileIndices = map.Terrain.ExtraBlendTileIndices.ToArray(),
            cliffInfoIndices = map.Terrain.CliffInfoIndices.ToArray(),
            cliffState = map.Terrain.CliffState.ToArray(),
            World = [.. map.World.Values],
            Sides = map.Sides.Select(CopySide).ToList(),
            Teams = map.Teams.Select(CopyTeam).ToList(),
            Objects = map.Objects.Select(CopyObject).ToList(),
            Triggers = map.Triggers.Select(CopyTrigger).ToList(),
            Scripts = map.Scripts.Select(CopyScriptList).ToList(),
            WaypointLinks = [.. map.WaypointLinks],
            Lighting = CopyLighting(map.Lighting),
            IsDirty = map.IsDirty,
        };
    }

    /// <summary>
    /// Restores the snapshot into a map document.
    /// </summary>
    /// <param name="map">The map document.</param>
    public void Restore(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Terrain.Width = TerrainWidth;
        map.Terrain.Height = TerrainHeight;
        map.Terrain.BorderSize = BorderSize;
        map.Terrain.Boundaries.Clear();
        map.Terrain.Boundaries.AddRange(Boundaries);
        map.Terrain.NumBitmapTiles = NumBitmapTiles;
        map.Terrain.NumBlendedTiles = NumBlendedTiles;
        map.Terrain.NumCliffInfo = NumCliffInfo;
        map.Terrain.NumEdgeTiles = NumEdgeTiles;
        map.Terrain.TextureClasses.Clear();
        map.Terrain.TextureClasses.AddRange(TextureClasses.Select(c => c with { }));
        map.Terrain.EdgeTextureClasses.Clear();
        map.Terrain.EdgeTextureClasses.AddRange(EdgeTextureClasses.Select(c => c with { }));
        map.Terrain.BlendTiles.Clear();
        map.Terrain.BlendTiles.AddRange(BlendTiles.Select(b => b with { }));
        map.Terrain.CliffInfos.Clear();
        map.Terrain.CliffInfos.AddRange(CliffInfos.Select(c => c with { U = c.U.ToArray() }));
        map.Terrain.Heights = (byte[])heights.Clone();
        map.Terrain.TileIndices = (short[])tileIndices.Clone();
        map.Terrain.BlendTileIndices = (short[])blendTileIndices.Clone();
        map.Terrain.ExtraBlendTileIndices = (short[])extraBlendTileIndices.Clone();
        map.Terrain.CliffInfoIndices = (short[])cliffInfoIndices.Clone();
        map.Terrain.CliffState = (byte[])cliffState.Clone();
        ReplaceDict(map.World, World);
        map.Sides.Clear();
        map.Sides.AddRange(Sides.Select(CopySide));
        map.Teams.Clear();
        map.Teams.AddRange(Teams.Select(CopyTeam));
        map.Objects.Clear();
        map.Objects.AddRange(Objects.Select(CopyObject));
        map.Triggers.Clear();
        map.Triggers.AddRange(Triggers.Select(CopyTrigger));
        map.Scripts.Clear();
        map.Scripts.AddRange(Scripts.Select(CopyScriptList));
        map.WaypointLinks.Clear();
        map.WaypointLinks.AddRange(WaypointLinks);
        map.Lighting = CopyLighting(Lighting);
        map.IsDirty = IsDirty;
    }

    private static void ReplaceDict(MapDict target, IReadOnlyList<MapDictValue> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value with { });
        }
    }

    private static MapSideEntry CopySide(MapSideEntry side)
    {
        var copy = new MapSideEntry();
        foreach (var value in side.Properties.Values)
        {
            copy.Properties.Add(value with { });
        }

        foreach (var entry in side.BuildList)
        {
            copy.BuildList.Add(CopyBuildListEntry(entry));
        }

        return copy;
    }

    private static MapBuildListEntry CopyBuildListEntry(MapBuildListEntry entry)
    {
        return new MapBuildListEntry
        {
            BuildingName = entry.BuildingName,
            TemplateName = entry.TemplateName,
            X = entry.X,
            Y = entry.Y,
            Z = entry.Z,
            Angle = entry.Angle,
            InitiallyBuilt = entry.InitiallyBuilt,
            NumRebuilds = entry.NumRebuilds,
            Script = entry.Script,
            Health = entry.Health,
            Whiner = entry.Whiner,
            Unsellable = entry.Unsellable,
            Repairable = entry.Repairable,
        };
    }

    private static MapTeamEntry CopyTeam(MapTeamEntry team)
    {
        var copy = new MapTeamEntry();
        foreach (var value in team.Properties.Values)
        {
            copy.Properties.Add(value with { });
        }

        return copy;
    }

    private static MapObjectEntry CopyObject(MapObjectEntry obj)
    {
        var copy = new MapObjectEntry
        {
            X = obj.X,
            Y = obj.Y,
            Z = obj.Z,
            Angle = obj.Angle,
            Flags = obj.Flags,
            Name = obj.Name,
        };
        foreach (var value in obj.Properties.Values)
        {
            copy.Properties.Add(value with { });
        }

        return copy;
    }

    private static MapTrigger CopyTrigger(MapTrigger trigger)
    {
        var copy = new MapTrigger
        {
            Name = trigger.Name,
            LayerName = trigger.LayerName,
            Id = trigger.Id,
            IsWaterArea = trigger.IsWaterArea,
            IsRiver = trigger.IsRiver,
            RiverStart = trigger.RiverStart,
        };
        copy.Points.AddRange(trigger.Points);
        return copy;
    }

    private static ScriptListModel CopyScriptList(ScriptListModel list)
    {
        var copy = new ScriptListModel();
        foreach (var group in list.Groups)
        {
            var groupCopy = new ScriptGroupModel
            {
                Name = group.Name,
                IsActive = group.IsActive,
                IsSubroutine = group.IsSubroutine,
            };
            foreach (var script in group.Scripts)
            {
                groupCopy.Scripts.Add(CopyScript(script));
            }

            copy.Groups.Add(groupCopy);
        }

        foreach (var script in list.Scripts)
        {
            copy.Scripts.Add(CopyScript(script));
        }

        return copy;
    }

    private static ScriptModel CopyScript(ScriptModel script)
    {
        var copy = new ScriptModel
        {
            Name = script.Name,
            Comment = script.Comment,
            ConditionComment = script.ConditionComment,
            ActionComment = script.ActionComment,
            IsActive = script.IsActive,
            IsOneShot = script.IsOneShot,
            Easy = script.Easy,
            Normal = script.Normal,
            Hard = script.Hard,
            IsSubroutine = script.IsSubroutine,
            DelaySeconds = script.DelaySeconds,
        };
        foreach (var branch in script.OrConditions)
        {
            var branchCopy = new ScriptOrBranch();
            foreach (var condition in branch.Conditions)
            {
                branchCopy.Conditions.Add(CopyCondition(condition));
            }

            copy.OrConditions.Add(branchCopy);
        }

        foreach (var action in script.ActionsTrue)
        {
            copy.ActionsTrue.Add(CopyAction(action));
        }

        foreach (var action in script.ActionsFalse)
        {
            copy.ActionsFalse.Add(CopyAction(action));
        }

        return copy;
    }

    private static ScriptCondition CopyCondition(ScriptCondition condition)
    {
        var copy = new ScriptCondition
        {
            ConditionType = condition.ConditionType,
            InternalName = condition.InternalName,
        };
        foreach (var parameter in condition.Parameters)
        {
            copy.Parameters.Add(CopyParameter(parameter));
        }

        return copy;
    }

    private static ScriptActionModel CopyAction(ScriptActionModel action)
    {
        var copy = new ScriptActionModel
        {
            ActionType = action.ActionType,
            InternalName = action.InternalName,
        };
        foreach (var parameter in action.Parameters)
        {
            copy.Parameters.Add(CopyParameter(parameter));
        }

        return copy;
    }

    private static ScriptParameter CopyParameter(ScriptParameter parameter)
    {
        return new ScriptParameter
        {
            Type = parameter.Type,
            IntValue = parameter.IntValue,
            RealValue = parameter.RealValue,
            StringValue = parameter.StringValue,
            CoordValue = parameter.CoordValue,
        };
    }

    private static MapLightingData CopyLighting(MapLightingData lighting)
    {
        var copy = new MapLightingData
        {
            TimeOfDay = lighting.TimeOfDay,
            ShadowColor = lighting.ShadowColor,
        };
        foreach (var time in lighting.TimesOfDay)
        {
            var timeCopy = new MapTimeOfDayLighting();
            foreach (var light in time.TerrainLights)
            {
                timeCopy.TerrainLights.Add(CopyLight(light));
            }

            foreach (var light in time.ObjectLights)
            {
                timeCopy.ObjectLights.Add(CopyLight(light));
            }

            copy.TimesOfDay.Add(timeCopy);
        }

        return copy;
    }

    private static MapLight CopyLight(MapLight light)
    {
        return new MapLight
        {
            AmbientR = light.AmbientR,
            AmbientG = light.AmbientG,
            AmbientB = light.AmbientB,
            DiffuseR = light.DiffuseR,
            DiffuseG = light.DiffuseG,
            DiffuseB = light.DiffuseB,
            PosX = light.PosX,
            PosY = light.PosY,
            PosZ = light.PosZ,
        };
    }
}
