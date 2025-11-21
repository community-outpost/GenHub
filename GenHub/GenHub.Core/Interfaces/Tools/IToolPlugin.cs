using Avalonia.Controls;
using GenHub.Core.Models.Tools;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Represents a tool that can be integrated into the application.
/// </summary>
public interface IToolPlugin
{
    /// <summary>
    /// Gets the metadata of the tool plugin.
    /// </summary>
    ToolMetadata Metadata { get; }

    /// <summary>
    /// Creates the UI control for the tool.
    /// This control will be hosted within the application's tool tab.
    /// </summary>
    /// <returns>The root control for the tool's UI.</returns>
    Control CreateControl();

    /// <summary>
    /// Called when the tool is activated or shown in the UI.
    /// Use this method to initialize resources or update the tool's state.
    /// </summary>
    /// <param name="serviceProvider">The service provider for accessing application services.</param>
    void OnActivated(IServiceProvider serviceProvider);

    /// <summary>
    /// Called when the tool is deactivated or hidden from the UI.
    /// Use this method to clean up resources or save the tool's state.
    /// </summary>
    void OnDeactivated();

    /// <summary>
    /// Called when the tool is being unloaded from the application.
    /// </summary>
    void Dispose();
}