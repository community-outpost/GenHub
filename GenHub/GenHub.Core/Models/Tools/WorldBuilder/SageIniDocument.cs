// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// The parsed content of one SAGE INI file: blocks with inheritance applied, plus the
/// diagnostics and skip lists collected while parsing.
/// </summary>
/// <param name="SourceName">The entry file the parse started from.</param>
/// <param name="Blocks">The parsed blocks in source order.</param>
/// <param name="Diagnostics">The notes collected while parsing.</param>
/// <param name="SkippedBlocks">Recognized blocks that could not finish in tolerant mode (the friend_getWBSkippedBlocks equivalent).</param>
/// <param name="UnrecognizedBlocks">Headers skipped because their token is not in the block table.</param>
public sealed record SageIniDocument(
    string SourceName,
    IReadOnlyList<SageIniBlock> Blocks,
    IReadOnlyList<SageIniDiagnostic> Diagnostics,
    IReadOnlyList<SageIniSkippedBlock> SkippedBlocks,
    IReadOnlyList<SageIniSkippedBlock> UnrecognizedBlocks)
{
    /// <summary>
    /// Gets a value indicating whether any error diagnostic was recorded.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Level == SageIniDiagnosticLevel.Error);
}
