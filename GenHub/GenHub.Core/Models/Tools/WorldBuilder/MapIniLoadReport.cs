// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Report of one WorldBuilder map.ini load (INI::loadWB semantics): applied blocks plus
/// the skipped-block lists. SkippedBlocks is the friend_getWBSkippedBlocks equivalent
/// (recognized blocks that could not finish); UnrecognizedBlocks extends the engine
/// report with headers outside the reduced block table.
/// </summary>
/// <param name="MapIniPath">The map.ini path that was loaded.</param>
/// <param name="BlocksLoaded">The number of blocks merged into the database.</param>
/// <param name="SkippedBlocks">Recognized blocks skipped because they could not finish.</param>
/// <param name="UnrecognizedBlocks">Headers skipped because their token is not in the reduced block table.</param>
/// <param name="Diagnostics">The notes collected while parsing.</param>
public sealed record MapIniLoadReport(
    string MapIniPath,
    int BlocksLoaded,
    IReadOnlyList<SageIniSkippedBlock> SkippedBlocks,
    IReadOnlyList<SageIniSkippedBlock> UnrecognizedBlocks,
    IReadOnlyList<SageIniDiagnostic> Diagnostics);
