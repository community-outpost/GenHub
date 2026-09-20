namespace GenHub.Core.Models.Online;

/// <summary>
/// How closely a local game profile matches a lobby's expected profile.
/// </summary>
public enum OnlineProfileMatch
{
    /// <summary>
    /// Either side advertised nothing; no statement is possible.
    /// </summary>
    Unknown,

    /// <summary>
    /// Same game client and same gameplay content (same exe and ini inputs).
    /// </summary>
    Exact,

    /// <summary>
    /// Same game client but different gameplay content. Joining still works;
    /// the game itself may refuse mismatched lobbies.
    /// </summary>
    SameClient,

    /// <summary>
    /// Different game clients. These setups cannot play together.
    /// </summary>
    Mismatch,
}
