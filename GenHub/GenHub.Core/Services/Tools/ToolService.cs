using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Service for managing tool plugins.
/// </summary>
public class ToolService : IToolService
{
    private readonly IToolPluginLoader _pluginLoader;
    private readonly IToolRegistry _toolRegistry;
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILogger<ToolService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolService"/> class.
    /// </summary>
    /// <param name="pluginLoader">Plugin loader for loading tool plugins.</param>
    /// <param name="toolRegistry">Registry for managing tool plugins.</param>
    /// <param name="userSettingsService">Service for managing user settings.</param>
    /// <param name="logger">Logger for logging tool service activities.</param>
    public ToolService(
        IToolPluginLoader pluginLoader,
        IToolRegistry toolRegistry,
        IUserSettingsService userSettingsService,
        ILogger<ToolService> logger)
    {
        _pluginLoader = pluginLoader;
        _toolRegistry = toolRegistry;
        _userSettingsService = userSettingsService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IToolPlugin>> AddToolAsync(string assemblyPath)
    {
        try
        {
            if (!_pluginLoader.ValidatePlugin(assemblyPath))
            {
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("Invalid tool plugin assembly."));
            }

            var plugin = _pluginLoader.LoadPluginFromAssembly(assemblyPath);
            if (plugin == null)
            {
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("Failed to load tool plugin from assembly."));
            }

            if (_toolRegistry.GetToolById(plugin.Metadata.Id) != null)
            {
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("A tool with the same ID is already registered."));
            }

            _toolRegistry.RegisterTool(plugin, assemblyPath);

            _userSettingsService.Update(settings =>
            {
                settings.InstalledToolAssemblyPaths ??= new List<string>();
                if (!settings.InstalledToolAssemblyPaths.Contains(assemblyPath))
                {
                    settings.InstalledToolAssemblyPaths.Add(assemblyPath);
                }
            });

            await _userSettingsService.SaveAsync();

            _logger.LogInformation("Tool plugin {PluginName} v{PluginVersion} added successfully.", plugin.Metadata.Name, plugin.Metadata.Version);
            return OperationResult<IToolPlugin>.CreateSuccess(plugin);
        }
        catch
        {
            _logger.LogError("An error occurred while adding tool plugin from assembly: {AssemblyPath}", assemblyPath);
            return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("An error occurred while adding the tool plugin."));
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IToolPlugin> GetAllTools()
    {
        return _toolRegistry.GetAllTools();
    }

    /// <inheritdoc/>
    public async Task<OperationResult<List<IToolPlugin>>> LoadSavedToolsAsync()
    {
        try
        {
            var settings = _userSettingsService.Get();
            var toolPaths = settings.InstalledToolAssemblyPaths ?? new List<string>();
            var loadedPlugins = new List<IToolPlugin>();

            foreach (var path in toolPaths)
            {
                var plugin = _pluginLoader.LoadPluginFromAssembly(path);
                if (plugin != null)
                {
                    _toolRegistry.RegisterTool(plugin, path);
                    loadedPlugins.Add(plugin);
                }
                else
                {
                    _logger.LogWarning("Failed to load tool plugin from saved path: {Path}", path);
                }
            }

            _logger.LogInformation("Loaded {Count} tool plugins from saved settings.", loadedPlugins.Count);
            return await Task.FromResult(OperationResult<List<IToolPlugin>>.CreateSuccess(loadedPlugins));
        }
        catch
        {
            _logger.LogError("An error occurred while loading saved tool plugins.");
            return await Task.FromResult(OperationResult<List<IToolPlugin>>.CreateFailure("An error occurred while loading saved tool plugins."));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveToolAsync(string toolId)
    {
        try
        {
            var assemblyPath = _toolRegistry.GetToolAssemblyPath(toolId);
            if (assemblyPath == null)
            {
                return await Task.FromResult(OperationResult<bool>.CreateFailure("Tool not found."));
            }

            if (!_toolRegistry.UnregisterTool(toolId))
            {
                return await Task.FromResult(OperationResult<bool>.CreateFailure("Failed to unregister tool."));
            }

            _userSettingsService.Update(settings =>
            {
                settings.InstalledToolAssemblyPaths?.Remove(assemblyPath);
            });

            await _userSettingsService.SaveAsync();

            _logger.LogInformation("Tool with ID {ToolId} removed successfully.", toolId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch
        {
            _logger.LogError("An error occurred while removing tool with ID: {ToolId}", toolId);
            return OperationResult<bool>.CreateFailure("An error occurred while removing the tool.");
        }
    }
}