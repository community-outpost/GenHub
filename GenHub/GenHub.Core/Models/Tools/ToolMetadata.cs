namespace GenHub.Core.Models.Tools;

/// <summary>
/// Metadata information for a tool plugin.
/// </summary>
public class ToolMetadata
{
    /// <summary>
    /// Gets or sets the unique identifier for the tool.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the tool.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the author of the tool.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// Gets or sets the version of the tool.
    /// </summary>
    public required string Author { get; set; }

    /// <summary>
    /// Gets or sets a description of the tool's functionality.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the icon path for the tool (relative to tool assembly or absolute).
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool is bundled with the application and cannot be removed.
    /// </summary>
    public bool IsBundled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool should be displayed in full screen mode (hiding default headers/margins).
    /// </summary>
    public bool IsFullScreen { get; set; }

    /// <summary>
    /// Gets or sets the tags/categories for the tool.
    /// </summary>
    public List<string> Tags { get; set; } = [];
}
