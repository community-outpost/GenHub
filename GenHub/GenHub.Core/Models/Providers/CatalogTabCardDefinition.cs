using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines a display card supplied by a publisher for a catalog custom tab.
/// </summary>
public class CatalogTabCardDefinition
{
    /// <summary>Gets or sets the card heading.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets supporting text for the card.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional local or remote image URL.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>Gets or sets an optional compact label displayed above the card title.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets the card accent colour in a format understood by Avalonia.</summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#303D59";
}
