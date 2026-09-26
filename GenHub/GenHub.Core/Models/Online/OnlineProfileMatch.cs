namespace GenHub.Core.Models.Online;

/// <summary>
/// How closely a local game profile matches a lobby's expected profile.
/// In C&amp;C Generals and Zero Hour multiplayer, network compatibility is binary:
/// either the game types and engine INI CRCs match, or players cannot play together without desync.
/// </summary>
public enum OnlineProfileMatch
{
    /// <summary>
    /// Either side advertised nothing; no statement is possible.
    /// </summary>
    Unknown,

    /// <summary>
    /// Matching INI CRC or identical profile content. The setups are network-compatible.
    /// </summary>
    Exact,

    /// <summary>
    /// Differing INI CRCs or different game types. These setups cannot play together.
    /// </summary>
    Mismatch,
}
