using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Successful join outcome: short-lived grant plus overlay configuration and
/// the initial member roster. Credential material must never be logged.
/// </summary>
public sealed record OnlineJoinResult
{
    /// <summary>
    /// Gets the joined network identifier.
    /// </summary>
    [JsonPropertyName("networkId")]
    public required string NetworkId { get; init; }

    /// <summary>
    /// Gets the short-lived join grant token (scope network:join, TTL minutes).
    /// </summary>
    [JsonPropertyName("grant")]
    public required string Grant { get; init; }

    /// <summary>
    /// Gets the grant expiry timestamp.
    /// </summary>
    [JsonPropertyName("grantExpiresUtc")]
    public DateTime GrantExpiresUtc { get; init; }

    /// <summary>
    /// Gets the assigned overlay IP address for this member.
    /// </summary>
    [JsonPropertyName("overlayIp")]
    public required string OverlayIp { get; init; }

    /// <summary>
    /// Gets the opaque overlay adapter configuration payload.
    /// </summary>
    [JsonPropertyName("adapterConfig")]
    public string AdapterConfig { get; init; } = string.Empty;

    /// <summary>
    /// Gets the initial member roster.
    /// </summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<OnlineMember> Members { get; init; } = [];

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
}
