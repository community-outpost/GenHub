// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// The object catalog (TheThingFactory): typed Object-family templates plus the
/// Side to EditorSorting to name object tree from ObjectOptions::addObject.
/// </summary>
public sealed class ThingTemplateCatalog(ISageIniDatabase database, IMappedImageRegistry mappedImages, ILogger<ThingTemplateCatalog> logger) : IThingTemplateCatalog
{
    private sealed class MutableBranch
    {
        public Dictionary<string, MutableBranch> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ThingTemplateInfo> Templates { get; } = [];

        public MutableBranch GetOrAdd(string name)
        {
            if (!Children.TryGetValue(name, out var child))
            {
                child = new MutableBranch();
                Children[name] = child;
            }

            return child;
        }
    }

    private const float DefaultAssetScale = 1.0f;

    private static readonly string[] ObjectFamilyTokens =
    [
        SageIniConstants.Inheritance.Object,
        SageIniConstants.Inheritance.ObjectReskin,
        SageIniConstants.Inheritance.ChildObject,
        SageIniConstants.Inheritance.ObjectExtend,
    ];

    /// <inheritdoc />
    public IReadOnlyList<ThingTemplateInfo> GetAll()
    {
        return CollectTemplates()
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public ThingTemplateInfo? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var token in ObjectFamilyTokens.Reverse())
        {
            var block = database.FindBlock(token, name);
            if (block is not null)
            {
                return ToInfo(block);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ObjectTreeNode BuildObjectTree(IEnumerable<string>? legacyModelNames = null)
    {
        var root = new MutableBranch();
        var testRoot = new MutableBranch();
        var templateCount = 0;
        foreach (var template in CollectTemplates())
        {
            templateCount++;
            var side = string.IsNullOrEmpty(template.Side) ? WorldBuilderCatalogConstants.ObjectTree.MissingSideName : template.Side;
            var sorting = string.IsNullOrEmpty(template.EditorSorting) ? WorldBuilderCatalogConstants.ObjectTree.UnsortedName : template.EditorSorting;
            (IsTest(template) ? testRoot : root).GetOrAdd(side).GetOrAdd(sorting).Templates.Add(template);
        }

        var children = new List<ObjectTreeNode>();
        if (testRoot.Children.Count > 0)
        {
            children.Add(new ObjectTreeNode(
                WorldBuilderCatalogConstants.ObjectTree.TestNodeName,
                ObjectTreeNodeKind.Test,
                null,
                BuildSideNodes(testRoot)));
        }

        children.AddRange(BuildSideNodes(root));
        var legacy = BuildLegacyNodes(legacyModelNames);
        if (legacy is not null)
        {
            children.Add(legacy);
        }

        logger.LogDebug("Built object tree with {Count} templates", templateCount);
        return new ObjectTreeNode(string.Empty, ObjectTreeNodeKind.Root, null, children);
    }

    /// <inheritdoc />
    public MappedImageDefinition? FindButtonImage(ThingTemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return string.IsNullOrWhiteSpace(template.ButtonImage) ? null : mappedImages.GetByName(template.ButtonImage);
    }

    /// <inheritdoc />
    public MappedImageDefinition? FindSelectPortrait(ThingTemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return string.IsNullOrWhiteSpace(template.SelectPortrait) ? null : mappedImages.GetByName(template.SelectPortrait);
    }

    private static bool IsTest(ThingTemplateInfo template)
    {
        return template.EditorSorting.Equals(WorldBuilderCatalogConstants.EditorSorting.Test, StringComparison.Ordinal);
    }

    private static string CanonicalSorting(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return WorldBuilderCatalogConstants.EditorSorting.Names
            .FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string? FindModelName(SageIniBlock block)
    {
        foreach (var subBlock in block.SubBlocks)
        {
            if (!subBlock.Key.Equals(SageIniConstants.ModuleOpeners.Draw, StringComparison.Ordinal))
            {
                continue;
            }

            if (subBlock.ModuleType.Equals(WorldBuilderCatalogConstants.DrawModules.W3DTreeDraw, StringComparison.OrdinalIgnoreCase))
            {
                return SageFieldParsers.FirstValue(subBlock, WorldBuilderCatalogConstants.DrawModules.ModelName);
            }

            return SageFieldParsers.FirstValue(subBlock, WorldBuilderCatalogConstants.DrawModules.Model);
        }

        return null;
    }

    private static List<ObjectTreeNode> BuildSideNodes(MutableBranch parent)
    {
        return parent.Children.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => BuildSideNode(entry.Key, entry.Value))
            .ToList();
    }

    private static ObjectTreeNode BuildSideNode(string side, MutableBranch sortings)
    {
        var children = sortings.Children.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ObjectTreeNode(
                entry.Key,
                ObjectTreeNodeKind.Sorting,
                null,
                entry.Value.Templates.OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(template => new ObjectTreeNode(template.Name, ObjectTreeNodeKind.Template, template, []))
                    .ToList()))
            .ToList();
        return new ObjectTreeNode(side, ObjectTreeNodeKind.Side, null, children);
    }

    private static ObjectTreeNode? BuildLegacyNodes(IEnumerable<string>? legacyModelNames)
    {
        var legacy = legacyModelNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        if (legacy is null || legacy.Count == 0)
        {
            return null;
        }

        return new ObjectTreeNode(
            WorldBuilderCatalogConstants.ObjectTree.LegacyModelsNodeName,
            ObjectTreeNodeKind.LegacyModels,
            null,
            legacy.Select(name => new ObjectTreeNode(name, ObjectTreeNodeKind.Template, null, [])).ToList());
    }

    private static ThingTemplateInfo ToInfo(SageIniBlock block)
    {
        return new ThingTemplateInfo(
            block.Name,
            block.BlockToken,
            block.ParentName,
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.DisplayName),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.Side) ?? string.Empty,
            CanonicalSorting(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.EditorSortingKey)),
            SageFieldParsers.AllValues(block, WorldBuilderCatalogConstants.ThingFields.KindOfKey),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.Buildable),
            SageFieldParsers.ParseInt(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.BuildCost)),
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.BuildTime)),
            SageFieldParsers.ParseColor(block, WorldBuilderCatalogConstants.ThingFields.DisplayColor),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.ButtonImage),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.SelectPortrait),
            SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.CommandSet),
            SageFieldParsers.AllValues(block, WorldBuilderCatalogConstants.ThingFields.BuildVariations),
            FindModelName(block),
            SageFieldParsers.ParseFloat(SageFieldParsers.FirstValue(block, WorldBuilderCatalogConstants.ThingFields.Scale)) ?? DefaultAssetScale);
    }

    private List<ThingTemplateInfo> CollectTemplates()
    {
        var byName = new Dictionary<string, ThingTemplateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ObjectFamilyTokens)
        {
            foreach (var block in database.GetBlocks(token))
            {
                byName[block.Name] = ToInfo(block);
            }
        }

        return byName.Values.ToList();
    }
}
