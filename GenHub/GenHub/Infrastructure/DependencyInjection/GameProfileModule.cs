using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for game profile services.
/// </summary>
public static class GameProfileModule
{
    /// <summary>
    /// Registers game profile services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configProvider">The configuration provider service.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGameProfileServices(this IServiceCollection services, IConfigurationProviderService configProvider)
    {
        // Get profiles directory from configuration
        var profilesDirectory = GetProfilesDirectory(configProvider);
        services.AddSingleton<IGameProfileRepository>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<GameProfileRepository>>();
            return new GameProfileRepository(profilesDirectory, logger);
        });
        services.AddScoped<IGameProfileManager, GameProfileManager>();
        services.AddSingleton<IGameProcessManager, GameProcessManager>();
        return services;
    }

    private static string GetProfilesDirectory(IConfigurationProviderService configProvider)
    {
        var appDataPath = configProvider.GetContentStoragePath();
        var profilesDirectory = Path.Combine(Path.GetDirectoryName(appDataPath) ?? string.Empty, "Profiles");
        Directory.CreateDirectory(profilesDirectory);
        return profilesDirectory;
    }
}
