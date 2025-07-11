using System.Collections.Generic;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Features.GameInstallations;
using GenHub.Features.GameVersions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        services.AddTransient<IGameVersionDetectionOrchestrator>(provider =>
        {
            var installationOrchestrator = provider.GetRequiredService<IGameInstallationDetectionOrchestrator>();
            var detector = provider.GetRequiredService<IGameVersionDetector>();
            var logger = provider.GetRequiredService<ILogger<GameVersionDetectionOrchestrator>>();
            return new GameVersionDetectionOrchestrator(installationOrchestrator, detector, logger);
        });

        return services;
    }
}
