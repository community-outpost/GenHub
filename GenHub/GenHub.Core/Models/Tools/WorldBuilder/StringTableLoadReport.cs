// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Report describing one string-table load: which sources contributed and how many
/// labels each provided. STR labels override same-named CSF labels; per-map map.str
/// labels override both.
/// </summary>
/// <param name="Language">The language directory the CSF path was derived from.</param>
/// <param name="CsfLabelCount">Labels loaded from the compiled CSF table.</param>
/// <param name="StrLabelCount">Labels loaded from the text STR source.</param>
/// <param name="MapStrLabelCount">Labels loaded from the per-map map.str overlay.</param>
public sealed record StringTableLoadReport(
    string Language,
    int CsfLabelCount,
    int StrLabelCount,
    int MapStrLabelCount)
{
    /// <summary>
    /// Gets the total labels loaded across all sources.
    /// </summary>
    public int TotalLabels => CsfLabelCount + StrLabelCount + MapStrLabelCount;
}
