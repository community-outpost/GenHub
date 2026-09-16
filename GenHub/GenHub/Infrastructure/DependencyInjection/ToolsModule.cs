using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Services.Tools;
using GenHub.Features.Tools;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
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

        // Register Publisher Studio services
        services.AddSingleton<IPublisherStudioService, PublisherStudioService>();
        services.AddSingleton<IPublisherStudioDialogService, PublisherStudioDialogService>();

        // Register Hosting Provider Factory for decentralized catalog distribution
        services.AddSingleton<IHostingProviderFactory, HostingProviderFactory>();
        services.AddSingleton<IHostingStateManager, HostingStateManager>();

        // Register ViewModels
        services.AddTransient<PublisherStudioViewModel>();

        // Register built-in tools
        services.AddSingleton<IToolPlugin, PublisherStudioTool>();

        return services;
    }
}
