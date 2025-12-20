using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines a mirror endpoint for downloads.
/// </summary>
public class MirrorEndpoint
{
    /// <summary>
    /// Gets or sets the mirror name for display.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mirror base URL.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the priority (lower = higher priority).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;
}
