// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One script action template: the compiled parameter list keyed by internal name,
/// with display overrides from the Scripts.ini ScriptAction block. The ordered
/// parameter types come from the engine ScriptEngine::init table; the INI only
/// overrides UI strings and help text.
/// </summary>
/// <param name="InternalName">The compiled internal name; the template key.</param>
/// <param name="UiName">The primary display name override; null when the INI sets none.</param>
/// <param name="UiName2">The secondary display name override; null when the INI sets none.</param>
/// <param name="HelpText">The editor help text override; null when the INI sets none.</param>
/// <param name="Parameters">The ordered parameter types (empty until the compiled table is ported).</param>
/// <param name="UiStrings">The UI lead-in strings interleaved with parameters (empty until the compiled table is ported).</param>
public sealed record ScriptActionTemplate(
    string InternalName,
    string? UiName,
    string? UiName2,
    string? HelpText,
    IReadOnlyList<WorldBuilderConstants.ScriptParameterType> Parameters,
    IReadOnlyList<string> UiStrings);
