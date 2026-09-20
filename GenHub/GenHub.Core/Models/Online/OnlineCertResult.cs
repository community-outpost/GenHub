using System;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Grant refresh outcome: a re-minted join grant with its new expiry.
/// Credential material must never be logged.
/// </summary>
public sealed record OnlineCertResult
{
    /// <summary>
    /// Gets the refreshed join grant token.
    /// </summary>
    [JsonPropertyName("grant")]
    public string Grant { get; init; } = string.Empty;

    /// <summary>
    /// Gets the refreshed grant expiry timestamp.
    /// </summary>
    [JsonPropertyName("grantExpiresUtc")]
    public DateTime GrantExpiresUtc { get; init; }
}
