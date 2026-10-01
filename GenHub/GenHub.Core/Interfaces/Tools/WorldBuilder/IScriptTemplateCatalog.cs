// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Script action and condition templates plus the EditParameter picker taxonomy.
/// Templates read live from the SAGE INI database; pickers backed by INI stores or
/// compiled engine tables return content, while pickers backed by per-map data
/// (scripts, teams, waypoints, units) return empty until map context is available.
/// </summary>
public interface IScriptTemplateCatalog
{
    /// <summary>
    /// Gets all script action templates ordered by internal name.
    /// </summary>
    /// <returns>The action templates in internal-name order.</returns>
    IReadOnlyList<ScriptActionTemplate> GetActions();

    /// <summary>
    /// Gets all script condition templates ordered by internal name.
    /// </summary>
    /// <returns>The condition templates in internal-name order.</returns>
    IReadOnlyList<ScriptConditionTemplate> GetConditions();

    /// <summary>
    /// Finds one action template by internal name (case-insensitive).
    /// </summary>
    /// <param name="internalName">The compiled internal name.</param>
    /// <returns>The template, or null when absent.</returns>
    ScriptActionTemplate? FindAction(string internalName);

    /// <summary>
    /// Finds one condition template by internal name (case-insensitive).
    /// </summary>
    /// <param name="internalName">The compiled internal name.</param>
    /// <returns>The template, or null when absent.</returns>
    ScriptConditionTemplate? FindCondition(string internalName);

    /// <summary>
    /// Gets the picker entries for one script parameter type.
    /// </summary>
    /// <param name="type">The parameter type.</param>
    /// <returns>The entries in picker order; empty for free-entry and per-map types.</returns>
    IReadOnlyList<string> GetPickList(WorldBuilderConstants.ScriptParameterType type);
}
