using System;
using System.Text.Json.Serialization;
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines endpoint URLs for a provider.
/// </summary>
public class ProviderEndpoints
{
    /// <summary>
    /// Gets or sets the catalog/API URL for discovering content.
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.CatalogUrl)]
    public string? CatalogUrl { get; set; }

    /// <summary>
    /// Gets or sets the base URL for downloads.
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.DownloadBaseUrl)]
    public string? DownloadBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the website URL for attribution.
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.WebsiteUrl)]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Gets or sets the support/contact URL.
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.SupportUrl)]
    public string? SupportUrl { get; set; }

    /// <summary>
    /// Gets or sets the latest version URL (for single-release providers).
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.LatestVersionUrl)]
    public string? LatestVersionUrl { get; set; }

    /// <summary>
    /// Gets or sets the manifest API URL (for JSON API providers).
    /// </summary>
    [JsonPropertyName(ProviderEndpointConstants.ManifestApiUrl)]
    public string? ManifestApiUrl { get; set; }

    /// <summary>
    /// Gets or sets additional named endpoints as key-value pairs.
    /// Allows providers to define custom endpoints beyond the standard ones.
    /// </summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, string> Custom { get; set; } = [];

    /// <summary>
    /// Gets or sets additional mirror base URLs.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<MirrorEndpoint> Mirrors { get; set; } = [];

    /// <summary>
    /// Gets an endpoint URL by name, checking both standard properties and custom endpoints.
    /// </summary>
    /// <param name="name">The endpoint name (case-insensitive).</param>
    /// <returns>The endpoint URL or null if not found.</returns>
    public string? GetEndpoint(string name)
    {
        // Check standard endpoints first
        if (string.Equals(name, ProviderEndpointConstants.CatalogUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.Catalog, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(CatalogUrl))
            {
                return CatalogUrl;
            }
        }

        if (string.Equals(name, ProviderEndpointConstants.DownloadBaseUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.DownloadBase, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(DownloadBaseUrl))
            {
                return DownloadBaseUrl;
            }
        }

        if (string.Equals(name, ProviderEndpointConstants.WebsiteUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.Website, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(WebsiteUrl))
            {
                return WebsiteUrl;
            }
        }

        if (string.Equals(name, ProviderEndpointConstants.SupportUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.Support, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(SupportUrl))
            {
                return SupportUrl;
            }
        }

        if (string.Equals(name, ProviderEndpointConstants.LatestVersionUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(LatestVersionUrl))
            {
                return LatestVersionUrl;
            }
        }

        if (string.Equals(name, ProviderEndpointConstants.ManifestApiUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProviderEndpointConstants.ManifestApi, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(ManifestApiUrl))
            {
                return ManifestApiUrl;
            }
        }

        // Check custom endpoints
        if (Custom.TryGetValue(name, out var customValue))
        {
            return customValue;
        }

        // Case-insensitive search in custom endpoints
        foreach (var kvp in Custom)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }
}
