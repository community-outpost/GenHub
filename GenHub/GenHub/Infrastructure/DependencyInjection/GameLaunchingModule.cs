using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.Launching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddLaunchingServices(this IServiceCollection services)
    {
        // LaunchRegistry is singleton - it tracks all launches globally across the app lifetime
        services.AddSingleton<ILaunchRegistry, LaunchRegistry>();

        // GameLauncher is scoped - one per request/operation to avoid captive dependencies
        // This prevents issues where scoped dependencies (like IGameProfileManager) are captured by singletons
        services.AddScoped<IGameLauncher, GameLauncher>();

        return services;
    }
}
