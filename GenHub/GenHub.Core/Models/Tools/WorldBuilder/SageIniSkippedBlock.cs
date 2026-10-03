// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A block header the tolerant map.ini loader skipped, with the reason it was skipped.
/// </summary>
/// <param name="HeaderLine">The verbatim header line that opened the skipped block.</param>
/// <param name="SourceFile">The file the header was read from.</param>
/// <param name="LineNumber">The 1-based line number of the header.</param>
/// <param name="Reason">Why the block was skipped.</param>
public sealed record SageIniSkippedBlock(string HeaderLine, string SourceFile, int LineNumber, string Reason);
