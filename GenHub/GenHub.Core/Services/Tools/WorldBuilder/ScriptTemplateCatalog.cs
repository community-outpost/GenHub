// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System.Collections.Frozen;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Script action and condition templates plus the EditParameter picker taxonomy.
/// </summary>
public sealed class ScriptTemplateCatalog(
    ISageIniDatabase database,
    IThingTemplateCatalog things,
    IStringTableService strings,
    ILogger<ScriptTemplateCatalog> logger) : IScriptTemplateCatalog
{
    private static readonly FrozenDictionary<WorldBuilderConstants.ScriptParameterType, string[]> CompiledPickLists =
        new Dictionary<WorldBuilderConstants.ScriptParameterType, string[]>
        {
            [WorldBuilderConstants.ScriptParameterType.Comparison] = WorldBuilderCatalogConstants.PickLists.Comparison,
            [WorldBuilderConstants.ScriptParameterType.AiMood] = WorldBuilderCatalogConstants.PickLists.AiMood,
            [WorldBuilderConstants.ScriptParameterType.SkirmishWaypointPath] = WorldBuilderCatalogConstants.PickLists.SkirmishWaypointPaths,
            [WorldBuilderConstants.ScriptParameterType.RadarEventType] = WorldBuilderCatalogConstants.PickLists.RadarEventTypes,
            [WorldBuilderConstants.ScriptParameterType.LeftOrRight] = WorldBuilderCatalogConstants.PickLists.LeftOrRight,
            [WorldBuilderConstants.ScriptParameterType.Relation] = WorldBuilderCatalogConstants.PickLists.Relation,
            [WorldBuilderConstants.ScriptParameterType.Buildable] = WorldBuilderCatalogConstants.PickLists.Buildable,
            [WorldBuilderConstants.ScriptParameterType.SurfacesAllowed] = WorldBuilderCatalogConstants.PickLists.Surfaces,
            [WorldBuilderConstants.ScriptParameterType.ShakeIntensity] = WorldBuilderCatalogConstants.PickLists.ShakeIntensities,
            [WorldBuilderConstants.ScriptParameterType.ObjectStatus] = WorldBuilderCatalogConstants.ObjectStatus.Names,
            [WorldBuilderConstants.ScriptParameterType.ObjectPanelFlag] = WorldBuilderCatalogConstants.PickLists.ObjectPanelFlags,
            [WorldBuilderConstants.ScriptParameterType.ScienceAvailability] = WorldBuilderCatalogConstants.PickLists.ScienceAvailability,
            [WorldBuilderConstants.ScriptParameterType.KindOf] = WorldBuilderCatalogConstants.KindOf.Names,
        }.ToFrozenDictionary();

    /// <inheritdoc />
    public IReadOnlyList<ScriptActionTemplate> GetActions()
    {
        return CollectActions()
            .OrderBy(template => template.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptConditionTemplate> GetConditions()
    {
        return CollectConditions()
            .OrderBy(template => template.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public ScriptActionTemplate? FindAction(string internalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        return GetActions().FirstOrDefault(template => template.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public ScriptConditionTemplate? FindCondition(string internalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        return GetConditions().FirstOrDefault(template => template.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetPickList(WorldBuilderConstants.ScriptParameterType type)
    {
        if (CompiledPickLists.TryGetValue(type, out var compiled))
        {
            return compiled;
        }

        return GetStorePickList(type)
            ?? GetAudioPickList(type)
            ?? GetScopedPickList(type)
            ?? [];
    }

    private static ScriptActionTemplate ToAction(SageIniBlock block)
    {
        return new ScriptActionTemplate(
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.InternalName) ?? block.Name,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.UIName),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.UIName2),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.HelpText),
            [],
            []);
    }

    private static ScriptConditionTemplate ToCondition(SageIniBlock block)
    {
        return new ScriptConditionTemplate(
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.InternalName) ?? block.Name,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.UIName),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.UIName2),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ScriptTemplateFields.HelpText),
            [],
            []);
    }

    private IReadOnlyList<string>? GetStorePickList(WorldBuilderConstants.ScriptParameterType type)
    {
        var token = type switch
        {
            WorldBuilderConstants.ScriptParameterType.Science => WorldBuilderCatalogConstants.Blocks.Science,
            WorldBuilderConstants.ScriptParameterType.Upgrade => WorldBuilderCatalogConstants.Blocks.Upgrade,
            WorldBuilderConstants.ScriptParameterType.SpecialPower => WorldBuilderCatalogConstants.Blocks.SpecialPower,
            WorldBuilderConstants.ScriptParameterType.CommandButton => WorldBuilderCatalogConstants.Blocks.CommandButton,
            WorldBuilderConstants.ScriptParameterType.Movie => WorldBuilderCatalogConstants.Blocks.Video,
            WorldBuilderConstants.ScriptParameterType.Emoticon => WorldBuilderCatalogConstants.Blocks.Animation,
            _ => null,
        };
        return token is null ? null : BlockNames(token);
    }

    private IReadOnlyList<string>? GetAudioPickList(WorldBuilderConstants.ScriptParameterType type)
    {
        return type switch
        {
            WorldBuilderConstants.ScriptParameterType.Sound => BlockNames(WorldBuilderCatalogConstants.Blocks.AudioEvent),
            WorldBuilderConstants.ScriptParameterType.Dialog => BlockNames(WorldBuilderCatalogConstants.Blocks.DialogEvent),
            WorldBuilderConstants.ScriptParameterType.Music => BlockNames(WorldBuilderCatalogConstants.Blocks.MusicTrack),
            WorldBuilderConstants.ScriptParameterType.LocalizedText => strings.GetLabels(),
            _ => null,
        };
    }

    private IReadOnlyList<string>? GetScopedPickList(WorldBuilderConstants.ScriptParameterType type)
    {
        return type switch
        {
            WorldBuilderConstants.ScriptParameterType.ObjectType => things.GetAll().Select(template => template.Name).ToList(),
            WorldBuilderConstants.ScriptParameterType.FactionName => FactionSides(),
            WorldBuilderConstants.ScriptParameterType.Side => WorldBuilderCatalogConstants.SymbolicNames.SidePlayers.Concat(FactionSides()).ToList(),
            WorldBuilderConstants.ScriptParameterType.Unit => new[] { WorldBuilderCatalogConstants.SymbolicNames.ThisObject },
            WorldBuilderConstants.ScriptParameterType.Team => new[] { WorldBuilderCatalogConstants.SymbolicNames.ThisTeam },
            _ => null,
        };
    }

    private List<string> BlockNames(string token)
    {
        return database.GetBlocks(token).Select(block => block.Name).ToList();
    }

    private List<string> FactionSides()
    {
        var sides = new List<string>();
        foreach (var block in database.GetBlocks(WorldBuilderCatalogConstants.Blocks.PlayerTemplate))
        {
            var side = SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.PlayerTemplateFields.Side);
            if (!string.IsNullOrEmpty(side) && !sides.Contains(side, StringComparer.OrdinalIgnoreCase))
            {
                sides.Add(side);
            }
        }

        return sides;
    }

    private List<ScriptActionTemplate> CollectActions()
    {
        var blocks = database.GetBlocks(WorldBuilderCatalogConstants.Blocks.ScriptAction);
        logger.LogDebug("Collected {Count} script actions", blocks.Count);
        return blocks.Select(ToAction).ToList();
    }

    private List<ScriptConditionTemplate> CollectConditions()
    {
        var blocks = database.GetBlocks(WorldBuilderCatalogConstants.Blocks.ScriptCondition);
        logger.LogDebug("Collected {Count} script conditions", blocks.Count);
        return blocks.Select(ToCondition).ToList();
    }
}
