// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Severity of a SAGE INI parse diagnostic.
/// </summary>
public enum SageIniDiagnosticLevel
{
    /// <summary>Informational note; parsing continued normally.</summary>
    Info,

    /// <summary>Recoverable problem; the affected line or field was skipped.</summary>
    Warning,

    /// <summary>File-fatal problem in strict mode; the file contributed no blocks.</summary>
    Error,
}
