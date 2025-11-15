using System.Reflection;
using System.Runtime.Loader;
using GenHub.Core.Interfaces.Tools;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Loader for tool plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ToolPluginLoader"/> class.
/// </remarks>
/// <param name="logger">Logger instance.</param>
public class ToolPluginLoader(ILogger<ToolPluginLoader> logger)
: IToolPluginLoader
{
    /// <inheritdoc/>
    public IToolPlugin? LoadPluginFromAssembly(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                logger.LogWarning("Assembly file does not exist: {AssemblyPath}", assemblyPath);
                return null;
            }

            // Get the directory containing the plugin assembly
            var pluginDirectory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                logger.LogWarning("Could not determine plugin directory for: {AssemblyPath}", assemblyPath);
                return null;
            }

            // Set up assembly resolution to load dependencies from the plugin directory
            ResolveEventHandler resolver = (sender, args) =>
            {
                var assemblyName = new AssemblyName(args.Name);

                // First, check if the assembly is already loaded (e.g., Avalonia assemblies from the main app)
                var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

                if (loadedAssembly != null)
                {
                    logger.LogDebug("Using already loaded assembly: {AssemblyName}", assemblyName.Name);
                    return loadedAssembly;
                }

                // If not loaded, try to load from the plugin directory
                var dependencyPath = Path.Combine(pluginDirectory, assemblyName.Name + ".dll");

                if (File.Exists(dependencyPath))
                {
                    logger.LogDebug("Resolving dependency: {DependencyName} from {DependencyPath}", assemblyName.Name, dependencyPath);
                    return Assembly.LoadFrom(dependencyPath);
                }

                return null;
            };

            // Register the resolver
            AppDomain.CurrentDomain.AssemblyResolve += resolver;

            try
            {
                // TODO: Implement security checks to the possible extend before loading the assembly
                var assembly = Assembly.LoadFrom(assemblyPath);

                var pluginType = assembly.GetTypes().FirstOrDefault(t => typeof(IToolPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (pluginType == null)
                {
                    logger.LogWarning("No IToolPlugin implementation found in assembly: {AssemblyPath}", assemblyPath);
                    return null;
                }

                var plugin = Activator.CreateInstance(pluginType) as IToolPlugin;

                if (plugin == null)
                {
                    logger.LogWarning("Failed to create instance of IToolPlugin from assembly: {AssemblyPath}", assemblyPath);
                    return null;
                }

                logger.LogInformation("Successfully loaded tool plugin: {PluginName} v{PluginVersion} from assembly: {AssemblyPath}", plugin.Metadata.Name, plugin.Metadata.Version, assemblyPath);

                return plugin;
            }
            finally
            {
                // Unregister the resolver
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading tool plugin from assembly: {AssemblyPath}", assemblyPath);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool ValidatePlugin(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return false;
            }

            if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            var hasPlugin = assembly.GetTypes().Any(t => typeof(IToolPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            return hasPlugin;
        }
        catch
        {
            logger.LogWarning("Validation failed for plugin assembly: {AssemblyPath}", assemblyPath);
            return false;
        }
    }
}