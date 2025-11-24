using System;
using GenHub.Core.Interfaces.GameInstallations;
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
    public static IServiceCollection AddLinuxServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameInstallationDetector, LinuxInstallationDetector>();

        return services;
    }
}