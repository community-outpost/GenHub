// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// The road and bridge catalog (TheTerrainRoads): typed Road and Bridge blocks.
/// </summary>
public sealed class RoadCatalog(ISageIniDatabase database, ILogger<RoadCatalog> logger) : IRoadCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<RoadInfo> GetRoads()
    {
        return CollectRoads()
            .OrderBy(road => road.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<BridgeInfo> GetBridges()
    {
        return CollectBridges()
            .OrderBy(bridge => bridge.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public RoadInfo? FindRoad(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var block = database.FindBlock(WorldBuilderCatalogConstants.Blocks.Road, name);
        return block is null ? null : ToRoad(block);
    }

    /// <inheritdoc />
    public BridgeInfo? FindBridge(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var block = database.FindBlock(WorldBuilderCatalogConstants.Blocks.Bridge, name);
        return block is null ? null : ToBridge(block);
    }

    private static RoadInfo ToRoad(SageIniBlock block)
    {
        return new RoadInfo(
            block.Name,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.RoadFields.Texture),
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.RoadFields.RoadWidth)),
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.RoadFields.RoadWidthInTexture)));
    }

    private static BridgeInfo ToBridge(SageIniBlock block)
    {
        return new BridgeInfo(
            block.Name,
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeScale)),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.ScaffoldObjectName),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.ScaffoldSupportObjectName),
            SageFieldParsers.ParseColor(block, WorldBuilderCatalogConstants.BridgeFields.RadarColor),
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.TransitionEffectsHeight)),
            SageFieldParsers.ParseInt(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.NumFXPerType)),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeModelName),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.Texture),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeModelNameDamaged),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.TextureDamaged),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeModelNameReallyDamaged),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.TextureReallyDamaged),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeModelNameBroken),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.TextureBroken),
            CollectTowers(block),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.DamagedToSound),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.RepairedToSound),
            SageFieldParsers.JoinedLines(block, WorldBuilderCatalogConstants.BridgeFields.TransitionToOCL),
            SageFieldParsers.JoinedLines(block, WorldBuilderCatalogConstants.BridgeFields.TransitionToFX),
            SageFieldParsers.ParsePercent(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.BridgeFields.BridgeHoleAreaPercentage)));
    }

    private static IReadOnlyList<string> CollectTowers(SageIniBlock block)
    {
        var keys = new[]
        {
            WorldBuilderCatalogConstants.BridgeFields.TowerObjectNameFromLeft,
            WorldBuilderCatalogConstants.BridgeFields.TowerObjectNameFromRight,
            WorldBuilderCatalogConstants.BridgeFields.TowerObjectNameToLeft,
            WorldBuilderCatalogConstants.BridgeFields.TowerObjectNameToRight,
        };
        var towers = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            var tower = SageFieldParsers.FirstValue(block, key);
            if (!string.IsNullOrEmpty(tower))
            {
                towers.Add(tower);
            }
        }

        return towers;
    }

    private List<RoadInfo> CollectRoads()
    {
        var blocks = database.GetBlocks(WorldBuilderCatalogConstants.Blocks.Road);
        logger.LogDebug("Collected {Count} roads", blocks.Count);
        return blocks.Select(ToRoad).ToList();
    }

    private List<BridgeInfo> CollectBridges()
    {
        var blocks = database.GetBlocks(WorldBuilderCatalogConstants.Blocks.Bridge);
        logger.LogDebug("Collected {Count} bridges", blocks.Count);
        return blocks.Select(ToBridge).ToList();
    }
}
