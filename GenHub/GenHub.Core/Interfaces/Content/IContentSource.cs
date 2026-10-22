using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// A marker interface for any service that can provide or discover content.
/// This allows for generic handling of all content sources and provides common properties.
/// </summary>
public interface IContentSource
{
    /// <summary>
    /// Gets the unique name of the content source (e.g., "GitHub", "ModDB", "Local Files").
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Gets a human-readable description of what this content source provides.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a value indicating whether this content source is currently enabled and operational.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the capabilities of this content source.
    /// </summary>
    ContentSourceCapabilities Capabilities { get; }
}
