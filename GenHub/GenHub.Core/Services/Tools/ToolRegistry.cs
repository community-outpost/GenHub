using System.Collections.Concurrent;
using GenHub.Core.Interfaces.Tools;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Registry for managing tool plugins.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolPlugin> _tools = new();
    private readonly ConcurrentDictionary<string, string> _toolAssemblyPaths = new();

    /// <inheritdoc/>
    public IReadOnlyList<IToolPlugin> GetAllTools()
    {
        return _tools.Values.ToList();
    }

    /// <inheritdoc/>
    public string? GetToolAssemblyPath(string toolId)
    {
        _toolAssemblyPaths.TryGetValue(toolId, out var path);
        return path;
    }

    /// <inheritdoc/>
    public IToolPlugin? GetToolById(string toolId)
    {
        _tools.TryGetValue(toolId, out var tool);
        return tool;
    }

    /// <inheritdoc/>
    public void RegisterTool(IToolPlugin plugin, string assemblyPath)
    {
        _tools[plugin.Metadata.Id] = plugin;
        _toolAssemblyPaths[plugin.Metadata.Id] = assemblyPath;
    }

    /// <inheritdoc/>
    public bool UnregisterTool(string toolId)
    {
        var removed = _tools.TryRemove(toolId, out var plugin);
        if (removed && plugin != null)
        {
            plugin.Dispose();
            _toolAssemblyPaths.TryRemove(toolId, out var path);
        }

        return removed;
    }
}