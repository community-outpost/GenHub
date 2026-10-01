// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// The object catalog (TheThingFactory): typed Object-family templates plus the
/// Side to EditorSorting to name object tree from ObjectOptions::addObject.
/// All members read live from the SAGE INI database, so map.ini reloads are
/// reflected without an explicit refresh. Preview exposes the model name and
/// asset scale only; 3D rendering belongs to the later renderer phase.
/// </summary>
public interface IThingTemplateCatalog
{
    /// <summary>
    /// Gets all templates (Object, ObjectReskin, ObjectExtend, ChildObject) ordered by name.
    /// </summary>
    /// <returns>The templates in name order.</returns>
    IReadOnlyList<ThingTemplateInfo> GetAll();

    /// <summary>
    /// Finds one template by name (case-insensitive).
    /// </summary>
    /// <param name="name">The template name.</param>
    /// <returns>The template, or null when absent.</returns>
    ThingTemplateInfo? FindByName(string name);

    /// <summary>
    /// Builds the object tree: TEST-sorted templates under a TEST root, then a Side
    /// tier, then an EditorSorting (or UNSORTED) tier, then template leaves. Legacy
    /// model names without a template go under the legacy models branch.
    /// </summary>
    /// <param name="legacyModelNames">Optional model-only entries without templates.</param>
    /// <returns>The tree root.</returns>
    ObjectTreeNode BuildObjectTree(IEnumerable<string>? legacyModelNames = null);

    /// <summary>
    /// Resolves the ButtonImage cameo through the mapped-image registry.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>The mapped image, or null when the template names none or it is unknown.</returns>
    MappedImageDefinition? FindButtonImage(ThingTemplateInfo template);

    /// <summary>
    /// Resolves the SelectPortrait image through the mapped-image registry.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>The mapped image, or null when the template names none or it is unknown.</returns>
    MappedImageDefinition? FindSelectPortrait(ThingTemplateInfo template);
}
