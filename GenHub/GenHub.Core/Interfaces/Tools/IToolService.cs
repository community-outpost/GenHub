using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Service for managing tool plugins in GenHub.
/// </summary>
public interface IToolManager
{
    /// <summary>
    /// Loads all saved tool plugins.
    /// </summary>
    /// <returns>A list of loaded tool plugins.</returns>
    Task<OperationResult<List<IToolPlugin>>> LoadSavedToolsAsync();

    /// <summary>
    /// Adds a new tool plugin from the specified assembly path.
    /// </summary>
    /// <param name="assemblyPath">Path to the tool assembly.</param>
    /// <returns>A result containing the added tool plugin.</returns>
    Task<OperationResult<IToolPlugin>> AddToolAsync(string assemblyPath);

    /// <summary>
    /// Removes a tool plugin by its ID.
    /// </summary>
    /// <param name="toolId">The ID of the tool to remove.</param>
    /// <returns>A result indicating whether the removal was successful.</returns>
    Task<OperationResult<bool>> RemoveToolAsync(string toolId);

    /// <summary>
    /// Gets all registered tool plugins.
    /// </summary>
    /// <returns>A read-only list of all registered tool plugins.</returns>
    IReadOnlyList<IToolPlugin> GetAllTools();
}