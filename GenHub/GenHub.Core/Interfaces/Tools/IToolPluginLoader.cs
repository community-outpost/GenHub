namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Loader interface for tool plugins.
/// </summary>
public interface IToolPluginLoader
{
    /// <summary>
    /// Loads a tool plugin from the specified assembly path.
    /// </summary>
    /// <param name="assemblyPath">The path to the plugin assembly.</param>
    /// <returns>The loaded tool plugin, or null if loading failed.</returns>
    IToolPlugin? LoadPluginFromAssembly(string assemblyPath);

    /// <summary>
    /// Validates the plugin assembly before loading.
    /// </summary>
    /// <param name="assemblyPath">The path to the plugin assembly.</param>
    /// <returns>True if the plugin is valid; otherwise, false.</returns>
    bool ValidatePlugin(string assemblyPath);
}