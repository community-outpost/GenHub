using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Request payload for creating a network. The password is verified server-side;
/// only an access grant is returned, never the stored verifier.
/// </summary>
public sealed record OnlineCreateNetworkRequest
{
    /// <summary>
    /// Gets the display name of the network.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the network password. Empty for unlisted private networks joined by invite code.
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets the maximum number of slots.
    /// </summary>
    [JsonPropertyName("slotsMax")]
    public int SlotsMax { get; init; }

    /// <summary>
    /// Gets the game and mod tags describing the expected profile.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the network is publicly listed.
    /// </summary>
    [JsonPropertyName("isPublic")]
    public bool IsPublic { get; init; }

    /// <summary>
    /// Gets the optional description and house rules.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the host reflexive endpoint (host:port) shared with members.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the host prefers relayed traffic.
    /// Relayed members publish no endpoint, hiding the public IP from peers.
    /// </summary>
    [JsonPropertyName("preferRelay")]
    public bool PreferRelay { get; init; } = true;
}
