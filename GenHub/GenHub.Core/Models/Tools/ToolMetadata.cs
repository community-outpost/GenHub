namespace GenHub.Core.Models.Tools;

/// <summary>
/// Metadata describing a tool.
/// </summary>
public sealed class ToolMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolMetadata"/> class.
    /// </summary>
    /// <param name="id">Unique identifier for the tool.</param>
    /// <param name="name">Display name of the tool.</param>
    /// <param name="description">Brief description of what the tool does.</param>
    /// <param name="category">Category for grouping related tools.</param>
    /// <param name="iconPath">Optional path to the tool's icon.</param>
    /// <param name="order">Sort order for display (lower numbers appear first).</param>
    public ToolMetadata(
        string id,
        string name,
        string description,
        string category = "General",
        string? iconPath = null,
        int order = 0)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        IconPath = iconPath;
        Order = order;
    }

    /// <summary>
    /// Gets the unique identifier for the tool.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display name of the tool.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the category for grouping related tools.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the path to the tool's icon (optional).
    /// </summary>
    public string? IconPath { get; }

    /// <summary>
    /// Gets the sort order for display (lower numbers appear first).
    /// </summary>
    public int Order { get; }
}
