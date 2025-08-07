using System;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Linux.Features.AppUpdate;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Provides Linux-specific service registrations.
/// </summary>
public static class LinuxServicesModule
{
    /// <summary>
    /// Registers Linux-specific services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configProvider">The shared configuration provider instance.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLinuxServices(
        this IServiceCollection services,
        IConfigurationProviderService configProvider)
    {
        // Configure HttpClient for Linux update installer using config provider
        var userAgent = configProvider.GetDownloadUserAgent();
        var timeout = TimeSpan.FromSeconds(configProvider.GetDownloadTimeoutSeconds());

        services.AddHttpClient<LinuxUpdateInstaller>(client =>
        {
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        });

        // Register Linux-specific services
        services.AddSingleton<IPlatformUpdateInstaller, LinuxUpdateInstaller>();
        services.AddSingleton<IGameInstallationDetector, LinuxInstallationDetector>();

        return services;
    }
}
