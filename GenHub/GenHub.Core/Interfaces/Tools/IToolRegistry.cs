using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Registry for managing and discovering available tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    /// <returns>Collection of all registered tools.</returns>
    IReadOnlyCollection<ITool> GetAllTools();

    /// <summary>
    /// Gets all enabled tools.
    /// </summary>
    /// <returns>Collection of enabled tools.</returns>
    IReadOnlyCollection<ITool> GetEnabledTools();

    /// <summary>
    /// Gets a tool by its unique identifier.
    /// </summary>
    /// <param name="id">The tool's unique identifier.</param>
    /// <returns>The tool if found; otherwise, null.</returns>
    ITool? GetToolById(string id);

    /// <summary>
    /// Registers a tool with the registry.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    void RegisterTool(ITool tool);

    /// <summary>
    /// Gets tools organized by category.
    /// </summary>
    /// <returns>Dictionary of categories and their associated tools.</returns>
    IReadOnlyDictionary<string, IReadOnlyCollection<ITool>> GetToolsByCategory();
}
