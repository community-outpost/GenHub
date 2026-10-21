// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single raw field line inside a SAGE INI block: the key token plus its value tokens.
/// Values stay untyped at this layer; typed catalogs interpret them per field table.
/// </summary>
/// <param name="Key">The field key (first token of the line).</param>
/// <param name="Values">The remaining tokens of the line.</param>
public sealed record SageIniField(string Key, IReadOnlyList<string> Values);
