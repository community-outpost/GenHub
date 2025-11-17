using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering game installation services.
/// </summary>
public static class GameInstallationModule
{
    /// <summary>
    /// Adds all game installation related services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddGameInstallation(this IServiceCollection services)
    {
        services.AddSingleton<IGameInstallationService, GameInstallationService>();
        services.AddSingleton<IGameInstallationDetectionOrchestrator, GameInstallationDetectionOrchestrator>();

        return services;
    }
}