// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// An End-terminated nested scope inside a SAGE INI block, such as an Object module
/// (Draw = W3DModelDraw ModuleTag_01 ... End) or a bare scope (ArmorSet ... End).
/// Bare scopes carry empty module type and tag. Condition-state scopes nested inside
/// Draw modules are flattened: their opener and inner fields stay in source order so
/// the first Model field is the default preview model.
/// </summary>
/// <param name="Key">The opener field key (for example, Draw).</param>
/// <param name="ModuleType">The module type token (for example, W3DModelDraw); empty for bare scopes.</param>
/// <param name="Tag">The module tag token (for example, ModuleTag_01); empty for bare scopes.</param>
/// <param name="Fields">The raw fields inside the nested scope.</param>
public sealed record SageIniSubBlock(string Key, string ModuleType, string Tag, IReadOnlyList<SageIniField> Fields);
