using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// The lobby's expected game profile, published by the host. Profile ids are
/// machine-local, so joiners match on the fingerprint (game client plus
/// gameplay content) instead of the id.
/// </summary>
public sealed record OnlineExpectedProfile
{
    /// <summary>
    /// Gets the host-local identifier of the expected game profile.
    /// Only meaningful on the host's machine.
    /// </summary>
    [JsonPropertyName("expectedProfileId")]
    public string ExpectedProfileId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cross-machine fingerprint of the expected profile.
    /// Empty when the host selected no profile.
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
