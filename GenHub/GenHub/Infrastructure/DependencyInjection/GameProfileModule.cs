using System;
using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameSettings;
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
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGameProfileServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameProfileRepository>(serviceProvider =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            var profilesDirectory = GetProfilesDirectory(configProvider);
            var logger = serviceProvider.GetRequiredService<ILogger<GameProfileRepository>>();
            return new GameProfileRepository(profilesDirectory, logger);
        });
        services.AddScoped<IGameProfileManager, GameProfileManager>();
        services.AddSingleton<IGameProcessManager, GameProcessManager>();
        services.AddScoped<IProfileLauncherFacade, ProfileLauncherFacade>();
        services.AddScoped<IProfileEditorFacade, ProfileEditorFacade>();
        services.AddScoped<IDependencyResolver, DependencyResolver>();
        services.AddScoped<IProfileContentService, ProfileContentService>();
        services.AddSingleton<IGameSettingsService, GameSettingsService>();
        services.AddSingleton<IContentDisplayFormatter, ContentDisplayFormatter>();
        services.AddScoped<IProfileContentLoader, ProfileContentLoader>();
        services.AddSingleton<ProfileResourceService>();

        return services;
    }

    private static string GetProfilesDirectory(IConfigurationProviderService configProvider)
    {
        try
        {
            var appDataPath = configProvider.GetContentStoragePath();
            var parentDirectory = Path.GetDirectoryName(appDataPath);
            if (string.IsNullOrEmpty(parentDirectory))
            {
                throw new InvalidOperationException($"Unable to determine parent directory for path: {appDataPath}");
            }

            var profilesDirectory = Path.Combine(parentDirectory, "Profiles");
            Directory.CreateDirectory(profilesDirectory);
            return profilesDirectory;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to create profiles directory: {ex.Message}", ex);
        }
    }
}