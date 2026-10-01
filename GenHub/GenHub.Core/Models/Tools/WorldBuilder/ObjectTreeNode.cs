// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// The tier a node occupies in the object tree built by ObjectOptions::addObject.
/// </summary>
public enum ObjectTreeNodeKind
{
    /// <summary>The invisible root.</summary>
    Root,

    /// <summary>The TEST node grouping test-sorted templates.</summary>
    Test,

    /// <summary>A default-owning-side tier.</summary>
    Side,

    /// <summary>An EditorSorting (or UNSORTED) tier.</summary>
    Sorting,

    /// <summary>A template leaf.</summary>
    Template,

    /// <summary>The legacy node grouping model-only entries without a template.</summary>
    LegacyModels,
}

/// <summary>
/// One node of the object tree: TEST root, Side tier, EditorSorting tier, template
/// leaf, or the legacy models branch. Children are sorted case-insensitively like
/// the TVI_SORT insertion order of the C++ tree view.
/// </summary>
/// <param name="Name">The node label (tier name or template name).</param>
/// <param name="Kind">The tier this node occupies.</param>
/// <param name="Template">The template for leaf nodes; null for branch nodes and legacy leaves.</param>
/// <param name="Children">The child nodes in display order.</param>
public sealed record ObjectTreeNode(
    string Name,
    ObjectTreeNodeKind Kind,
    ThingTemplateInfo? Template,
    IReadOnlyList<ObjectTreeNode> Children);
