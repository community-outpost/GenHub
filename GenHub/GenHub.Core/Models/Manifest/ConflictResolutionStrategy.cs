namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Defines strategies for resolving content conflicts.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// Block installation entirely - user must remove conflicting content.
    /// </summary>
    Block = 0,

    /// <summary>
    /// Warn the user but allow installation to proceed.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Automatically prefer the newer version.
    /// </summary>
    PreferNewer = 2,

    /// <summary>
    /// Keep the existing content, don't install the new one.
    /// </summary>
    PreferExisting = 3,

    /// <summary>
    /// Let the user decide which content to keep.
    /// </summary>
    UserChoice = 4,

    /// <summary>
    /// Automatically merge content if possible.
    /// </summary>
    Merge = 5,
}
