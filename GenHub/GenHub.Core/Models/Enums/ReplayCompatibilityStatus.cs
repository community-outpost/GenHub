namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the compatibility status of a replay file against known and installed game clients and profiles.
/// </summary>
public enum ReplayCompatibilityStatus
{
    /// <summary>
    /// Compatibility has not yet been resolved or metadata is insufficient.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A matching game client and profile is configured and ready to play the replay.
    /// </summary>
    Compatible = 1,

    /// <summary>
    /// A matching game client is installed, but a dedicated profile needs to be created.
    /// </summary>
    RequiresProfile = 2,

    /// <summary>
    /// A matching game client is known in the catalog and available for download via CDN.
    /// </summary>
    Downloadable = 3,

    /// <summary>
    /// The replay has valid CRC metadata, but no matching game client or INI configuration is known in the catalog (CRC mismatch risk).
    /// </summary>
    Orphaned = 4,
}
