using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for game launching services.
/// </summary>
public static class GameLaunchingModule
{
    /// <summary>
    /// Registers launching services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configProvider">The configuration provider service.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddLaunchingServices(this IServiceCollection services, IConfigurationProviderService configProvider)
    {
        services.AddSingleton<ILaunchRegistry, LaunchRegistry>();
        services.AddScoped<IGameLauncher, GameLauncher>();
        return services;
    }
}
