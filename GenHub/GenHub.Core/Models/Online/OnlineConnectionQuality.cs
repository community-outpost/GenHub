namespace GenHub.Core.Models.Online;

/// <summary>
/// Aggregate connection quality for a network or member.
/// </summary>
public enum OnlineConnectionQuality
{
    /// <summary>
    /// Quality is not known yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// Peers connect directly (hole-punched P2P).
    /// </summary>
    Direct,

    /// <summary>
    /// Peers connect through a relay (privacy mode or restrictive NAT).
    /// </summary>
    Relay,

    /// <summary>
    /// Connection is being established.
    /// </summary>
    Connecting,
}
