namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the compatibility status of a replay file against known and installed game clients.
/// </summary>
public enum ReplayCompatibilityStatus
{
    /// <summary>
    /// Compatibility has not yet been resolved or metadata is insufficient.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A matching game client is installed and ready to play the replay.
    /// </summary>
    Compatible = 1,

    /// <summary>
    /// A matching game client is known and available for download via CDN, but not currently installed.
    /// </summary>
    Downloadable = 2,

    /// <summary>
    /// The replay has valid CRC metadata, but no matching game client is known in the catalog.
    /// </summary>
    Orphaned = 3,
}
