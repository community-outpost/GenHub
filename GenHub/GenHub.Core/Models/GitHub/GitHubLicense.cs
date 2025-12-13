using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents license information for a repository.
/// </summary>
public class GitHubLicense
{
    /// <summary>
    /// Gets or sets the license key (e.g., "mit", "gpl-3.0").
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the license name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SPDX ID.
    /// </summary>
    [JsonPropertyName("spdx_id")]
    public string SpdxId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the license URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the GraphQL node ID.
    /// </summary>
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;
}