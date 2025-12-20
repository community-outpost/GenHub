using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Configuration for author-based content discovery.
/// Used by dynamic providers like GitHub, ModDB, CNCLabs.
/// </summary>
public class DiscoveryConfiguration
{
    /// <summary>
    /// Gets or sets the discovery method (e.g., "github-topic", "moddb-search", "cnclabs-api").
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets topics/tags to search for (for topic-based discovery).
    /// </summary>
    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Gets or sets the search query template.
    /// </summary>
    [JsonPropertyName("searchQueryTemplate")]
    public string? SearchQueryTemplate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authors become dynamic publishers.
    /// </summary>
    [JsonPropertyName("authorsAsPublishers")]
    public bool AuthorsAsPublishers { get; set; } = true;

    /// <summary>
    /// Gets or sets the content type mapping rules.
    /// </summary>
    [JsonPropertyName("contentTypeRules")]
    public List<ContentTypeRule> ContentTypeRules { get; set; } = new();
}
