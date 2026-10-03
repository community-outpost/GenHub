// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Editor-relevant view of one Object-family template (Object, ObjectReskin,
/// ObjectExtend, ChildObject), typed from the raw SAGE INI block per
/// ThingTemplate::s_objectFieldParseTable. Inheritance is already resolved by the
/// parser, so reskins carry their parent gameplay fields plus their own visuals.
/// </summary>
/// <param name="Name">The template name (the object-tree leaf).</param>
/// <param name="BlockToken">The source block token (Object, ObjectReskin, ObjectExtend, ChildObject).</param>
/// <param name="ParentName">The inheritance parent name for reskin, extend, and child blocks; otherwise null.</param>
/// <param name="DisplayName">The raw DisplayName INI value (usually LABEL: plus a CSF key); null when absent.</param>
/// <param name="Side">The default owning side (first tree tier); empty when absent.</param>
/// <param name="EditorSorting">The EditorSorting value (second tree tier); empty when absent or unrecognized.</param>
/// <param name="KindOf">The KindOf flag tokens in source order.</param>
/// <param name="Buildable">The raw Buildable value; null when absent.</param>
/// <param name="BuildCost">The build cost in credits; null when absent or unparsable.</param>
/// <param name="BuildTime">The build time in seconds; null when absent or unparsable.</param>
/// <param name="DisplayColor">The tree and preview color as ARGB; null when absent or unparsable.</param>
/// <param name="ButtonImage">The MappedImage name for the command-button cameo; null when absent.</param>
/// <param name="SelectPortrait">The MappedImage name for the selection portrait; null when absent.</param>
/// <param name="CommandSet">The command button set name; null when absent.</param>
/// <param name="BuildVariations">Alternate template names in source order.</param>
/// <param name="ModelName">The best preview model from the first Draw module; null when the template has no model.</param>
/// <param name="AssetScale">The asset scale applied to the preview model.</param>
public sealed record ThingTemplateInfo(
    string Name,
    string BlockToken,
    string? ParentName,
    string? DisplayName,
    string Side,
    string EditorSorting,
    IReadOnlyList<string> KindOf,
    string? Buildable,
    int? BuildCost,
    float? BuildTime,
    int? DisplayColor,
    string? ButtonImage,
    string? SelectPortrait,
    string? CommandSet,
    IReadOnlyList<string> BuildVariations,
    string? ModelName,
    float AssetScale)
{
    /// <summary>
    /// Gets a value indicating whether this template is a visual-only reskin of its parent.
    /// </summary>
    public bool IsReskin => BlockToken.Equals(SageIniConstants.Inheritance.ObjectReskin, StringComparison.Ordinal);
}
