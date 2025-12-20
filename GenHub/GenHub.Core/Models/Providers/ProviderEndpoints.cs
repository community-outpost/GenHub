using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines endpoint URLs for a provider.
/// </summary>
public class ProviderEndpoints
{
    /// <summary>
    /// Gets or sets the catalog/API URL for discovering content.
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; set; }

    /// <summary>
    /// Gets or sets the base URL for downloads.
    /// </summary>
    [JsonPropertyName("downloadBaseUrl")]
    public string? DownloadBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the website URL for attribution.
    /// </summary>
    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Gets or sets the support/contact URL.
    /// </summary>
    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; set; }

    /// <summary>
    /// Gets or sets the latest version URL (for single-release providers).
    /// </summary>
    [JsonPropertyName("latestVersionUrl")]
    public string? LatestVersionUrl { get; set; }

    /// <summary>
    /// Gets or sets the manifest API URL (for JSON API providers).
    /// </summary>
    [JsonPropertyName("manifestApiUrl")]
    public string? ManifestApiUrl { get; set; }

    /// <summary>
    /// Gets or sets additional named endpoints as key-value pairs.
    /// Allows providers to define custom endpoints beyond the standard ones.
    /// </summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, string> Custom { get; set; } = new();

    /// <summary>
    /// Gets or sets additional mirror base URLs.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<MirrorEndpoint> Mirrors { get; set; } = new();

    /// <summary>
    /// Gets an endpoint URL by name, checking both standard properties and custom endpoints.
    /// </summary>
    /// <param name="name">The endpoint name (case-insensitive).</param>
    /// <returns>The endpoint URL or null if not found.</returns>
    public string? GetEndpoint(string name)
    {
        // Check standard endpoints first
        var result = name.ToLowerInvariant() switch
        {
            "catalogurl" or "catalog" => this.CatalogUrl,
            "downloadbaseurl" or "downloadbase" => this.DownloadBaseUrl,
            "websiteurl" or "website" => this.WebsiteUrl,
            "supporturl" or "support" => this.SupportUrl,
            "latestversionurl" or "latestversion" => this.LatestVersionUrl,
            "manifestapiurl" or "manifestapi" => this.ManifestApiUrl,
            _ => null,
        };

        if (result != null)
        {
            return result;
        }

        // Check custom endpoints
        if (this.Custom.TryGetValue(name, out var customValue))
        {
            return customValue;
        }

        // Case-insensitive search in custom endpoints
        foreach (var kvp in this.Custom)
        {
            if (kvp.Key.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }
}
