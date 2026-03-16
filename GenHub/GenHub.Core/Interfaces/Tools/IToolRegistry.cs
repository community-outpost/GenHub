namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Registry for managing installed tool plugins.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all registered tool plugins.
    /// </summary>
    /// <returns>A read-only list of all registered tool plugins.</returns>
    IReadOnlyList<IToolPlugin> GetAllTools();

    /// <summary>
    /// Gets a tool plugin by its ID.
    /// </summary>
    /// <param name="toolId">The ID of the tool plugin.</param>
    /// <returns>The tool plugin with the specified ID, or null if not found.</returns>
    IToolPlugin? GetToolById(string toolId);

    /// <summary>
    /// Registers a new tool plugin with an assembly path (external tool).
    /// </summary>
    /// <param name="plugin">The tool plugin to register.</param>
    /// <param name="assemblyPath">The path to the tool assembly.</param>
    void RegisterTool(IToolPlugin plugin, string assemblyPath);

    /// <summary>
    /// Registers a new built-in tool plugin.
    /// </summary>
    /// <param name="plugin">The tool plugin to register.</param>
    void RegisterTool(IToolPlugin plugin);

    /// <summary>
    /// Unregisters a tool plugin by its ID.
    /// </summary>
    /// <param name="toolId">The ID of the tool to unregister.</param>
    /// <returns>True if the tool was unregistered successfully, false otherwise.</returns>
    bool UnregisterTool(string toolId);

    /// <summary>
    /// Gets the assembly path of a tool plugin by its ID.
    /// </summary>
    /// <param name="toolId">The ID of the tool plugin.</param>
    /// <returns>The assembly path of the tool plugin, or null if not found.</returns>
    string? GetToolAssemblyPath(string toolId);
}