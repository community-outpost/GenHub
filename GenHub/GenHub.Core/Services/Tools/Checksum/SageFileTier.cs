namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Defines the priority tier of files and archives mounted in the SAGE virtual file system.
/// Higher numerical tier values strictly take precedence over lower ones.
/// </summary>
public enum SageFileTier
{
    /// <summary>
    /// Base game vanilla assets (e.g. Generals vanilla when Zero Hour is active).
    /// </summary>
    BaseGame = 0,

    /// <summary>
    /// Expansion / active game target assets (e.g. Zero Hour or standalone Generals).
    /// </summary>
    Expansion = 1,

    /// <summary>
    /// User mod files, project folders, linked archives, and release outputs.
    /// </summary>
    Mod = 2,
}
