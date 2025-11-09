using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Service for managing tool plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ToolService"/> class.
/// </remarks>
/// <param name="pluginLoader">Plugin loader for loading tool plugins.</param>
/// <param name="toolRegistry">Registry for managing tool plugins.</param>
/// <param name="userSettingsService">Service for managing user settings.</param>
/// <param name="logger">Logger for logging tool service activities.</param>
public class ToolService(
    IToolPluginLoader pluginLoader,
    IToolRegistry toolRegistry,
    IUserSettingsService userSettingsService,
    ILogger<ToolService> logger)
: IToolManager
{
    /// <inheritdoc/>
    public async Task<OperationResult<IToolPlugin>> AddToolAsync(string assemblyPath)
    {
        try
        {
            logger.LogInformation("Adding tool plugin from assembly: {AssemblyPath}", assemblyPath);

            if (!pluginLoader.ValidatePlugin(assemblyPath))
            {
                logger.LogWarning("Tool plugin validation failed for: {AssemblyPath}", assemblyPath);
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("Invalid tool plugin assembly."));
            }

            var plugin = pluginLoader.LoadPluginFromAssembly(assemblyPath);
            if (plugin == null)
            {
                logger.LogWarning("Failed to load tool plugin from assembly: {AssemblyPath}", assemblyPath);
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("Failed to load tool plugin from assembly."));
            }

            if (toolRegistry.GetToolById(plugin.Metadata.Id) != null)
            {
                logger.LogWarning("Tool with ID {ToolId} is already registered", plugin.Metadata.Id);
                return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("A tool with the same ID is already registered."));
            }

            toolRegistry.RegisterTool(plugin, assemblyPath);
            logger.LogDebug("Tool {ToolName} registered in registry", plugin.Metadata.Name);

            userSettingsService.Update(settings =>
            {
                settings.InstalledToolAssemblyPaths ??= new List<string>();
                if (!settings.InstalledToolAssemblyPaths.Contains(assemblyPath))
                {
                    settings.InstalledToolAssemblyPaths.Add(assemblyPath);
                    logger.LogDebug("Added {AssemblyPath} to InstalledToolAssemblyPaths. Total count: {Count}", assemblyPath, settings.InstalledToolAssemblyPaths.Count);
                }
                else
                {
                    logger.LogDebug("Assembly path {AssemblyPath} already exists in InstalledToolAssemblyPaths", assemblyPath);
                }
            });

            await userSettingsService.SaveAsync();
            logger.LogInformation("User settings saved after adding tool plugin");

            logger.LogInformation("Tool plugin {PluginName} v{PluginVersion} added successfully.", plugin.Metadata.Name, plugin.Metadata.Version);
            return OperationResult<IToolPlugin>.CreateSuccess(plugin);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while adding tool plugin from assembly: {AssemblyPath}", assemblyPath);
            return await Task.FromResult(OperationResult<IToolPlugin>.CreateFailure("An error occurred while adding the tool plugin."));
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IToolPlugin> GetAllTools()
    {
        return toolRegistry.GetAllTools();
    }

    /// <inheritdoc/>
    public async Task<OperationResult<List<IToolPlugin>>> LoadSavedToolsAsync()
    {
        try
        {
            var settings = userSettingsService.Get();
            var toolPaths = settings.InstalledToolAssemblyPaths ?? new List<string>();

            logger.LogInformation("Loading saved tool plugins. Found {Count} paths in settings.", toolPaths.Count);

            if (toolPaths.Count > 0)
            {
                logger.LogDebug("Tool paths: {Paths}", string.Join(", ", toolPaths));
            }

            var loadedPlugins = new List<IToolPlugin>();

            foreach (var path in toolPaths)
            {
                logger.LogDebug("Processing tool path: {Path}", path);

                // Check if tool is already loaded in registry
                var existingTools = toolRegistry.GetAllTools();
                var existingTool = existingTools.FirstOrDefault(t => toolRegistry.GetToolAssemblyPath(t.Metadata.Id) == path);

                if (existingTool != null)
                {
                    // Tool already loaded, reuse it
                    loadedPlugins.Add(existingTool);
                    logger.LogDebug("Tool plugin from {Path} already loaded, reusing existing instance.", path);
                    continue;
                }

                // Load new plugin
                var plugin = pluginLoader.LoadPluginFromAssembly(path);
                if (plugin != null)
                {
                    toolRegistry.RegisterTool(plugin, path);
                    loadedPlugins.Add(plugin);
                    logger.LogDebug("Loaded tool plugin {PluginName} from {Path}", plugin.Metadata.Name, path);
                }
                else
                {
                    logger.LogWarning("Failed to load tool plugin from saved path: {Path}", path);
                }
            }

            logger.LogInformation("Loaded {Count} tool plugins from saved settings.", loadedPlugins.Count);
            return await Task.FromResult(OperationResult<List<IToolPlugin>>.CreateSuccess(loadedPlugins));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while loading saved tool plugins.");
            return await Task.FromResult(OperationResult<List<IToolPlugin>>.CreateFailure("An error occurred while loading saved tool plugins."));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveToolAsync(string toolId)
    {
        try
        {
            var assemblyPath = toolRegistry.GetToolAssemblyPath(toolId);
            if (assemblyPath == null)
            {
                return await Task.FromResult(OperationResult<bool>.CreateFailure("Tool not found."));
            }

            if (!toolRegistry.UnregisterTool(toolId))
            {
                return await Task.FromResult(OperationResult<bool>.CreateFailure("Failed to unregister tool."));
            }

            userSettingsService.Update(settings =>
            {
                settings.InstalledToolAssemblyPaths?.Remove(assemblyPath);
            });

            await userSettingsService.SaveAsync();

            logger.LogInformation("Tool with ID {ToolId} removed successfully.", toolId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch
        {
            logger.LogError("An error occurred while removing tool with ID: {ToolId}", toolId);
            return OperationResult<bool>.CreateFailure("An error occurred while removing the tool.");
        }
    }
}
