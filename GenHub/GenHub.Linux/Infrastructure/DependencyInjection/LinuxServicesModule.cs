using System;
using System.Runtime.Versioning;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Linux.Features.Shortcuts;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IShortcutService, LinuxShortcutService>();

        return services;
    }
}