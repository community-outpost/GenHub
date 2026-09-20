using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Pre-join detail for a network. Still carries no endpoints: description, rules,
/// slot counts, and the expected game profile only.
/// </summary>
public sealed record OnlineNetworkDetail
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
    /// Gets the optional description and house rules.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

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
    /// Gets the identifier of the expected game profile for launch integration.
    /// </summary>
    [JsonPropertyName("expectedProfileId")]
    public string ExpectedProfileId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cross-machine fingerprint of the expected profile.
    /// </summary>
    [JsonPropertyName("expectedProfileFingerprint")]
    public string ExpectedProfileFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the expected profile.
    /// </summary>
    [JsonPropertyName("expectedProfileName")]
    public string ExpectedProfileName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the game client key of the expected profile.
    /// </summary>
    [JsonPropertyName("expectedGameClientId")]
    public string ExpectedGameClientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the gameplay content ids of the expected profile.
    /// </summary>
    [JsonPropertyName("expectedContentIds")]
    public IReadOnlyList<string> ExpectedContentIds { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the network requires a password.
    /// </summary>
    [JsonPropertyName("requiresPassword")]
    public bool RequiresPassword { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host is currently present.
    /// </summary>
    [JsonPropertyName("hostPresent")]
    public bool HostPresent { get; init; }
}
