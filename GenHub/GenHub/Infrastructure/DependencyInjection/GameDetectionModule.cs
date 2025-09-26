using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Features.GameClients;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for game detection services.
/// </summary>
public static class GameDetectionModule
{
    /// <summary>
    /// Adds game detection services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddGameDetectionService(this IServiceCollection services)
    {
        // Register orchestrators with logging
        services.AddTransient<IGameInstallationDetectionOrchestrator>(provider =>
        {
            var detectors = provider.GetRequiredService<IEnumerable<IGameInstallationDetector>>();
            var logger = provider.GetRequiredService<ILogger<GameInstallationDetectionOrchestrator>>();
            return new GameInstallationDetectionOrchestrator(detectors, logger);
        });

        services.AddTransient<IGameClientDetectionOrchestrator>(provider =>
        {
            var installationOrchestrator = provider.GetRequiredService<IGameInstallationDetectionOrchestrator>();
            var detector = provider.GetRequiredService<IGameClientDetector>();
            var logger = provider.GetRequiredService<ILogger<GameClientDetectionOrchestrator>>();
            return new GameClientDetectionOrchestrator(installationOrchestrator, detector, logger);
        });

        return services;
    }
}
