using GenHub.Common.Services;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.Services;
using GenHub.Features.GameSettings;
using GenHub.Features.Workspace;
using GenHub.MacOS.Features.GitHub.Services;
using GenHub.MacOS.Features.Shortcuts;
using GenHub.MacOS.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;

namespace GenHub.MacOS.Infrastructure.DependencyInjection;

/// <summary>
/// Registers services implemented specifically for macOS.
/// </summary>
public static class MacOSServicesModule
{
    /// <summary>
    /// Registers macOS platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [SupportedOSPlatform("macos")]
    public static IServiceCollection AddMacOSServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameInstallationDetector, MacOSInstallationDetector>();
        services.AddSingleton<IGitHubTokenStorage, MacOSGitHubTokenStorage>();
        services.AddSingleton<IGamePathProvider, MacOSGamePathProvider>();
        services.AddSingleton<ISymlinkCapabilityProvider, UnixSymlinkCapabilityProvider>();
        services.AddSingleton<IShortcutService, MacOSShortcutService>();
        services.Replace(ServiceDescriptor.Singleton<IInstallationLocationTracker, FileInstallationLocationTracker>());
        services.Replace(ServiceDescriptor.Singleton<IInstallationSearchPathProvider, MacOSInstallationSearchPathProvider>());

        // Real hard links via link(2). Without this the base implementation throws, which
        // is deliberate: silently copying made a missing registration invisible while
        // every workspace consumed a full copy of the game.
        services.AddScoped<IFileOperationsService>(serviceProvider =>
        {
            var baseService = serviceProvider.GetRequiredService<FileOperationsService>();
            var casService = serviceProvider.GetRequiredService<ICasService>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnixFileOperationsService>>();
            return new UnixFileOperationsService(baseService, casService, logger);
        });

        // Disables self-update on macOS, which publishes no update artifacts.
        // AppServices.ConfigureApplicationServices invokes the platform module after
        // AddAppUpdateModule, so this registration supersedes VelopackUpdateManager.
        // Delete this line once macOS artifacts are published.
        services.AddSingleton<IVelopackUpdateManager, UnsupportedPlatformUpdateManager>();

        return services;
    }
}
