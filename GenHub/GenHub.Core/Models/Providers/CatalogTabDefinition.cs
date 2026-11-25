using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines a custom tab in a publisher's catalog.
/// This is the JSON representation that publishers use in their catalog.json files.
/// </summary>
public class CatalogTabDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this tab.
    /// </summary>
    [JsonPropertyName("tabId")]
    public string TabId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the tab header.
    /// </summary>
    [JsonPropertyName("header")]
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the icon name or path for the tab (optional).
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the order/priority of the tab (lower numbers appear first).
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; } = 100;

    /// <summary>
    /// Gets or sets the tab content type.
    /// Valid values: "custom", "files", "addons", "videos", "images", "reviews", "articles", "richtext", "webview".
    /// </summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "custom";

    /// <summary>
    /// Gets or sets the data source URL for the tab content (optional).
    /// Can be a catalog URL, API endpoint, or web page URL.
    /// </summary>
    [JsonPropertyName("dataSourceUrl")]
    public string? DataSourceUrl { get; set; }

    /// <summary>
    /// Gets or sets the content template identifier.
    /// Used to determine which UI template to use for rendering.
    /// </summary>
    [JsonPropertyName("contentTemplate")]
    public string? ContentTemplate { get; set; }

    /// <summary>
    /// Gets or sets introductory copy displayed above the tab cards.
    /// </summary>
    [JsonPropertyName("intro")]
    public string? Intro { get; set; }

    /// <summary>
    /// Gets or sets optional Markdown documentation displayed below the intro.
    /// Links open only for HTTP and HTTPS URLs, and images load only from safe remote HTTP and HTTPS URLs.
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the display cards supplied by the publisher for this tab.
    /// </summary>
    [JsonPropertyName("cards")]
    public List<CatalogTabCardDefinition> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets custom metadata for the tab.
    /// Can be used to pass additional configuration to the tab renderer.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the tab should be visible by default.
    /// </summary>
    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the tab content should be lazy-loaded.
    /// </summary>
    [JsonPropertyName("lazyLoad")]
    public bool LazyLoad { get; set; } = true;

    /// <summary>
    /// Gets or sets the content IDs a catalog-level tab applies to.
    /// A catalog-level tab applies only to the content IDs listed here, so an empty list applies to no item.
    /// Tabs declared on a content item ignore this list.
    /// </summary>
    [JsonPropertyName("appliesTo")]
    public List<string> AppliesTo { get; set; } = [];
}
