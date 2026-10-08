// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single parse note carrying the source position of the offending line.
/// </summary>
/// <param name="Level">The diagnostic severity.</param>
/// <param name="SourceFile">The file the line was read from.</param>
/// <param name="LineNumber">The 1-based line number within that file.</param>
/// <param name="Message">The human-readable description.</param>
public sealed record SageIniDiagnostic(SageIniDiagnosticLevel Level, string SourceFile, int LineNumber, string Message);
