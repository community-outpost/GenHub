// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// The terrain material palette (TheTerrainTypes): typed Terrain blocks.
/// </summary>
public sealed class TerrainTypeCatalog(ISageIniDatabase database, ILogger<TerrainTypeCatalog> logger) : ITerrainTypeCatalog
{
    private const float DefaultGlintStrength = 1.0f;
    private const float DefaultGlintGloss = 0.0f;

    /// <inheritdoc />
    public IReadOnlyList<TerrainTypeInfo> GetAll()
    {
        return Collect()
            .OrderBy(terrain => terrain.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<TerrainTypeInfo> GetPaletteEntries()
    {
        return Collect()
            .Where(terrain => !terrain.IsBlendEdge)
            .OrderBy(ClassRank)
            .ThenBy(terrain => terrain.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public TerrainTypeInfo? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var block = database.FindBlock(WorldBuilderCatalogConstants.Blocks.Terrain, name);
        return block is null ? null : ToInfo(block);
    }

    private static int ClassRank(TerrainTypeInfo terrain)
    {
        if (string.IsNullOrEmpty(terrain.Class))
        {
            return int.MaxValue;
        }

        var rank = Array.FindIndex(
            WorldBuilderCatalogConstants.TerrainClasses.Names,
            candidate => candidate.Equals(terrain.Class, StringComparison.OrdinalIgnoreCase));
        return rank < 0 ? int.MaxValue : rank;
    }

    private static TerrainTypeInfo ToInfo(SageIniBlock block)
    {
        var info = new TerrainTypeInfo(
            block.Name,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.Texture),
            SageFieldParsers.ParseBool(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.BlendEdges)) ?? false,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.Class),
            SageFieldParsers.ParseBool(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.RestrictConstruction)) ?? false,
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.GlintStrength)) ?? DefaultGlintStrength,
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.TerrainFields.GlintGloss)) ?? DefaultGlintGloss);
        return info;
    }

    private List<TerrainTypeInfo> Collect()
    {
        var blocks = database.GetBlocks(WorldBuilderCatalogConstants.Blocks.Terrain);
        logger.LogDebug("Collected {Count} terrain types", blocks.Count);
        return blocks.Select(ToInfo).ToList();
    }
}
