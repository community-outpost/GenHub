using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Timeout configuration for provider operations.
/// </summary>
public class ProviderTimeouts
{
    /// <summary>
    /// Gets or sets the catalog download timeout in seconds.
    /// </summary>
    [JsonPropertyName("catalogTimeoutSeconds")]
    public int CatalogTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the content download timeout in seconds.
    /// </summary>
    [JsonPropertyName("contentTimeoutSeconds")]
    public int ContentTimeoutSeconds { get; set; } = 300;
}
