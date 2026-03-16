using GenHub.Core.Interfaces.Launcher;
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
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddLaunchingServices(this IServiceCollection services)
    {
        // LaunchRegistry is singleton - it tracks all launches globally across the app lifetime
        services.AddSingleton<ILaunchRegistry, LaunchRegistry>();

        // GameLauncher is scoped - one per request/operation to avoid captive dependencies
        // This prevents issues where scoped dependencies (like IGameProfileManager) are captured by singletons
        services.AddScoped<IGameLauncher, GameLauncher>();

        // SteamLauncher for Steam integration - provisions files directly to game installation
        services.AddScoped<ISteamLauncher, SteamLauncher>();

        return services;
    }
}
