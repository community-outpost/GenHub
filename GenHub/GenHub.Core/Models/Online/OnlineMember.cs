using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// A joined member of a network roster. Overlay IPs are visible to fellow
/// members by design (like LAN peers); underlay endpoints never appear here.
/// </summary>
public sealed record OnlineMember
{
    /// <summary>
    /// Gets the member display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the member overlay IP address (virtual LAN address, not the real IP).
    /// </summary>
    [JsonPropertyName("overlayIp")]
    public required string OverlayIp { get; init; }

    /// <summary>
    /// Gets the member connection quality.
    /// </summary>
    [JsonPropertyName("quality")]
    public OnlineConnectionQuality Quality { get; init; } = OnlineConnectionQuality.Unknown;

    /// <summary>
    /// Gets a value indicating whether this member is the network host.
    /// </summary>
    [JsonPropertyName("isHost")]
    public bool IsHost { get; init; }

    /// <summary>
    /// Gets the member reflexive endpoint (host:port) for direct reachability
    /// checks. Empty when the member hides it (relay mode). Grant-scoped only.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the fingerprint of the member's selected game profile
    /// (game client plus gameplay content). Empty when unadvertised.
    /// </summary>
    [JsonPropertyName("profileFingerprint")]
    public string ProfileFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the member's selected game profile.
    /// </summary>
    [JsonPropertyName("profileName")]
    public string ProfileName { get; init; } = string.Empty;
}
