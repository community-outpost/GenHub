using System;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Windows.Features.AppUpdate;
using GenHub.Windows.Features.Workspace;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Windows.Infrastructure.DependencyInjection;

/// <summary>
/// Provides Windows-specific service registrations.
/// </summary>
public static class WindowsServicesModule
{
    /// <summary>
    /// Registers Windows-specific services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configProvider">The shared configuration provider instance.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddWindowsServices(
        this IServiceCollection services,
        IConfigurationProviderService configProvider)
    {
        // Configure HttpClient for Windows update installer using config provider
        var userAgent = configProvider.GetDownloadUserAgent();
        var timeout = TimeSpan.FromSeconds(configProvider.GetDownloadTimeoutSeconds());

        services.AddHttpClient<WindowsUpdateInstaller>(client =>
        {
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        });

        // Register Windows-specific services
        services.AddSingleton<IPlatformUpdateInstaller, WindowsUpdateInstaller>();
        services.AddSingleton<IGameInstallationDetector, WindowsInstallationDetector>();
        services.AddSingleton<IFileOperationsService, WindowsFileOperationsService>();

        return services;
    }
}
