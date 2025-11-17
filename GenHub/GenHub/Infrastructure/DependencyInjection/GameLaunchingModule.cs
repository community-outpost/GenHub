using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
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
        services.AddSingleton<ILaunchRegistry, LaunchRegistry>();
        services.AddScoped<IGameLauncher>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GameLauncher>>();
            var profileManager = sp.GetRequiredService<IGameProfileManager>();
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var processManager = sp.GetRequiredService<IGameProcessManager>();
            var manifestPool = sp.GetRequiredService<IContentManifestPool>();
            var launchRegistry = sp.GetRequiredService<ILaunchRegistry>();
            var gameInstallationService = sp.GetRequiredService<IGameInstallationService>();
            var manifestProvider = sp.GetRequiredService<IManifestProvider>();
            var casService = sp.GetRequiredService<ICasService>();
            var config = sp.GetRequiredService<IConfigurationProviderService>();
            return new GameLauncher(logger, profileManager, workspaceManager, processManager, manifestPool, launchRegistry, gameInstallationService, manifestProvider, casService, config);
        });
        return services;
    }
}