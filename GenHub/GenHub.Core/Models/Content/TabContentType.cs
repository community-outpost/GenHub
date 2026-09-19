namespace GenHub.Core.Models.Content;

/// <summary>
/// Defines the type of content a custom tab displays.
/// </summary>
public enum TabContentType
{
    /// <summary>
    /// Custom content with a specific template.
    /// </summary>
    Custom,

    /// <summary>
    /// List of downloadable files.
    /// </summary>
    Files,

    /// <summary>
    /// List of related addons or mods.
    /// </summary>
    Addons,

    /// <summary>
    /// Video gallery.
    /// </summary>
    Videos,

    /// <summary>
    /// Image gallery.
    /// </summary>
    Images,

    /// <summary>
    /// User reviews and ratings.
    /// </summary>
    Reviews,

    /// <summary>
    /// News articles and updates.
    /// </summary>
    Articles,

    /// <summary>
    /// HTML or markdown content.
    /// </summary>
    RichText,

    /// <summary>
    /// External web content (iframe).
    /// </summary>
    WebView,
}
