// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Report of one subsystem boot-table load: per-subsystem entries in boot order plus the
/// totals and diagnostics collected across every file read.
/// </summary>
/// <param name="Subsystems">Per-subsystem entries in the order they were loaded.</param>
/// <param name="FilesRead">The number of files successfully parsed.</param>
/// <param name="BlocksLoaded">The number of blocks merged into the database.</param>
/// <param name="Diagnostics">The notes collected across every file read.</param>
public sealed record SageIniLoadReport(
    IReadOnlyList<SageIniLoadReport.SubsystemLoadEntry> Subsystems,
    int FilesRead,
    int BlocksLoaded,
    IReadOnlyList<SageIniDiagnostic> Diagnostics)
{
    /// <summary>
    /// One subsystem row of the boot table.
    /// </summary>
    /// <param name="Subsystem">The subsystem short name.</param>
    /// <param name="FilesRead">The virtual paths parsed for this subsystem, Default files first.</param>
    /// <param name="BlocksLoaded">The number of blocks this subsystem contributed.</param>
    /// <param name="Skipped">True when the subsystem contributed nothing because its directories are absent from the mounted layers.</param>
    /// <param name="SkipReason">Why the subsystem was skipped; null when it was loaded.</param>
    public sealed record SubsystemLoadEntry(
        string Subsystem,
        IReadOnlyList<string> FilesRead,
        int BlocksLoaded,
        bool Skipped,
        string? SkipReason);
}
