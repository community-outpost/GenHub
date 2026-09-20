using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Directory entry for a public network. By design this carries metadata only:
/// no member lists, no overlay-to-underlay mappings, no endpoints or candidates.
/// Browsing the directory must never disclose underlay IPs.
/// </summary>
public sealed record OnlineNetworkSummary
{
    /// <summary>
    /// Gets the stable network identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of the network.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the game and mod tags describing the expected profile.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets the number of currently occupied slots.
    /// </summary>
    [JsonPropertyName("slotsUsed")]
    public int SlotsUsed { get; init; }

    /// <summary>
    /// Gets the maximum number of slots.
    /// </summary>
    [JsonPropertyName("slotsMax")]
    public int SlotsMax { get; init; }

    /// <summary>
    /// Gets the region label reported by the host.
    /// </summary>
    [JsonPropertyName("region")]
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the hosting player.
    /// </summary>
    [JsonPropertyName("hostDisplayName")]
    public string HostDisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the aggregate connection quality across members.
    /// </summary>
    [JsonPropertyName("quality")]
    public OnlineConnectionQuality Quality { get; init; } = OnlineConnectionQuality.Unknown;

    /// <summary>
    /// Gets a value indicating whether the network requires a password.
    /// </summary>
    [JsonPropertyName("requiresPassword")]
    public bool RequiresPassword { get; init; }

    /// <summary>
    /// Gets the last heartbeat timestamp reported by the network.
    /// </summary>
    [JsonPropertyName("lastHeartbeatUtc")]
    public DateTime LastHeartbeatUtc { get; init; }
}
