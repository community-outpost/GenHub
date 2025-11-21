namespace GenHub.Core.Models.Tools;

/// <summary>
/// Metadata information for a tool plugin.
/// </summary>
public class ToolMetadata
{
    /// <summary>
    /// Gets or sets the unique identifier for the tool.
    /// </summary>
    required public string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the tool.
    /// </summary>
    required public string Name { get; set; }

    /// <summary>
    /// Gets or sets the author of the tool.
    /// </summary>
    required public string Version { get; set; }

    /// <summary>
    /// Gets or sets the version of the tool.
    /// </summary>
    required public string Author { get; set; }

    /// <summary>
    /// Gets or sets a description of the tool's functionality.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the icon path for the tool (relative to tool assembly or absolute).
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the tags/categories for the tool.
    /// </summary>
    public List<string> Tags { get; set; } = new();
}