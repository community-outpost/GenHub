using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents the response from GitHub's Search Repositories API.
/// </summary>
public class GitHubRepositorySearchResponse
{
    /// <summary>
    /// Gets or sets the total count of matching repositories.
    /// </summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether results are incomplete due to timeout.
    /// </summary>
    [JsonPropertyName("incomplete_results")]
    public bool IncompleteResults { get; set; }

    /// <summary>
    /// Gets or sets the list of repository items.
    /// </summary>
    [JsonPropertyName("items")]
    public List<GitHubRepositorySearchItem> Items { get; set; } = new();
}