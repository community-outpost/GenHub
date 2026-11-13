namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Defines the priority tier of files and archives mounted in the SAGE virtual file system.
/// Higher numerical tier values strictly take precedence over lower ones.
/// </summary>
public enum SageFileTier
{
    /// <summary>
    /// Base game vanilla assets (the Generals install for Generals targets, or an
    /// explicit fallback root for non-Zero Hour targets).
    /// </summary>
    BaseGame = 0,

    /// <summary>
    /// Expansion / active game target assets (e.g. Zero Hour or standalone Generals).
    /// </summary>
    Expansion = 1,

    /// <summary>
    /// User mod files, project folders, and release outputs.
    /// </summary>
    Mod = 2,

    /// <summary>
    /// Explicitly linked .BIG archives, taking top precedence over project files.
    /// Linked loose folders resolve by mount order rather than tier, so this tier
    /// only guarantees precedence for archive entries.
    /// </summary>
    LinkedAsset = 3,
}
