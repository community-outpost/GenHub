using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.GameSettings;
using GenHub.Features.Workspace;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.Features.GitHub.Services;
using GenHub.Linux.Features.Shortcuts;
using GenHub.Linux.Features.Storage;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;

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
        services.AddSingleton<IGitHubTokenStorage, LinuxGitHubTokenStorage>();
        services.AddSingleton<IGamePathProvider, LinuxGamePathProvider>();
        services.AddSingleton<ISymlinkCapabilityProvider, UnixSymlinkCapabilityProvider>();
        services.AddSingleton<IShortcutService, LinuxShortcutService>();
        services.Replace(ServiceDescriptor.Singleton<IInstallationLocationTracker, LinuxInstallationTracker>());
        services.Replace(ServiceDescriptor.Singleton<IInstallationSearchPathProvider, LinuxInstallationSearchPathProvider>());

        services.AddUnixFileOperations();

        return services;
    }
}
