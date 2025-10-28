using GenHub.Core.Models.Tools;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Represents a tool that can be registered and displayed in the Tools tab.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the metadata describing this tool.
    /// </summary>
    ToolMetadata Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether this tool is currently enabled.
    /// Tools can be disabled based on application state or user settings.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Creates a new instance of the view model for this tool.
    /// This is called when the tool is selected by the user.
    /// </summary>
    /// <returns>A new view model instance for this tool.</returns>
    IToolViewModel CreateViewModel();
}
