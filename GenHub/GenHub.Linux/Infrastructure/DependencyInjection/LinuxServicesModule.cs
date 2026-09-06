using System;
using System.Runtime.Versioning;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.GameSettings;
using GenHub.Features.Workspace;
using GenHub.Linux.Features.Shortcuts;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering Linux-specific services.
/// </summary>
public static class LinuxServicesModule
{
    /// <summary>
    /// Registers Linux-specific services in the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [SupportedOSPlatform("linux")]
    public static IServiceCollection AddLinuxServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameInstallationDetector, LinuxInstallationDetector>();
        services.AddSingleton<IGamePathProvider, LinuxGamePathProvider>();
        services.AddSingleton<ISymlinkCapabilityProvider, UnixSymlinkCapabilityProvider>();
        services.AddSingleton<IShortcutService, LinuxShortcutService>();

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

        return services;
    }
}
