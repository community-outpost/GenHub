using GenHub.Core.Interfaces.Tools;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Registry for managing and discovering available tools.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ILogger<ToolRegistry>? _logger;
    private readonly Dictionary<string, ITool> _tools = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ITool> GetAllTools()
    {
        return _tools.Values
            .OrderBy(t => t.Metadata.Order)
            .ThenBy(t => t.Metadata.Name)
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ITool> GetEnabledTools()
    {
        return _tools.Values
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.Metadata.Order)
            .ThenBy(t => t.Metadata.Name)
            .ToList();
    }

    /// <inheritdoc/>
    public ITool? GetToolById(string id)
    {
        _tools.TryGetValue(id, out var tool);
        return tool;
    }

    /// <inheritdoc/>
    public void RegisterTool(ITool tool)
    {
        if (_tools.ContainsKey(tool.Metadata.Id))
        {
            _logger?.LogWarning(
                "Tool with ID '{ToolId}' is already registered. Skipping.",
                tool.Metadata.Id);
            return;
        }

        _tools[tool.Metadata.Id] = tool;
        _logger?.LogInformation(
            "Registered tool: {ToolName} (ID: {ToolId})",
            tool.Metadata.Name,
            tool.Metadata.Id);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IReadOnlyCollection<ITool>> GetToolsByCategory()
    {
        return _tools.Values
            .GroupBy(t => t.Metadata.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<ITool>)g
                    .OrderBy(t => t.Metadata.Order)
                    .ThenBy(t => t.Metadata.Name)
                    .ToList());
    }
}
