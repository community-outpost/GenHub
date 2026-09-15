namespace GenHub.Core.Models.Content;

/// <summary>
/// Defines a custom tab for content detail pages.
/// </summary>
public class CustomTabDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this tab.
    /// </summary>
    public required string TabId { get; set; }

    /// <summary>
    /// Gets or sets the display name shown in the tab header.
    /// </summary>
    public required string Header { get; set; }

    /// <summary>
    /// Gets or sets the icon name or path for the tab (optional).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the order/priority of the tab (lower numbers appear first).
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// Gets or sets the tab content type.
    /// </summary>
    public TabContentType ContentType { get; set; } = TabContentType.Custom;

    /// <summary>
    /// Gets or sets the data source URL for the tab content (optional).
    /// Can be a catalog URL, API endpoint, or web page URL.
    /// </summary>
    public string? DataSourceUrl { get; set; }

    /// <summary>
    /// Gets or sets the content template identifier.
    /// Used to determine which UI template to use for rendering.
    /// </summary>
    public string? ContentTemplate { get; set; }

    /// <summary>
    /// Gets or sets introductory copy displayed above the tab cards.
    /// </summary>
    public string? Intro { get; set; }

    /// <summary>
    /// Gets or sets the display cards supplied by the publisher for this tab.
    /// </summary>
    public List<CustomTabCardDefinition> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets custom metadata for the tab.
    /// Can be used to pass additional configuration to the tab renderer.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the tab should be visible.
    /// Can be used for conditional visibility based on content availability.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the tab content should be lazy-loaded.
    /// </summary>
    public bool LazyLoad { get; set; } = true;

    /// <summary>
    /// Gets or sets the function to load tab data dynamically.
    /// </summary>
    public Func<object>? DataLoader { get; set; }
}
