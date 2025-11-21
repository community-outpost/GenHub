using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Services.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for tool plugin services.
/// </summary>
public static class ToolsModule
{
    /// <summary>
    /// Registers tool plugin services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddToolsServices(this IServiceCollection services)
    {
        services.AddSingleton<IToolPluginLoader, ToolPluginLoader>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolManager, ToolService>();

        return services;
    }
}