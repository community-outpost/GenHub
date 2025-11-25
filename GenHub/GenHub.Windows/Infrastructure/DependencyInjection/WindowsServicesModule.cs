using System;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.Workspace;
using GenHub.Windows.Features.AppUpdate;
using GenHub.Windows.Features.GitHub.Services;
using GenHub.Windows.Features.Workspace;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddWindowsServices(this IServiceCollection services)
    {
        // Configure HttpClient for Windows update installer using config provider
        services.AddHttpClient<WindowsUpdateInstaller>((serviceProvider, client) =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            client.Timeout = TimeSpan.FromSeconds(configProvider.GetDownloadTimeoutSeconds());

            var userAgent = configProvider.GetDownloadUserAgent();
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        });

        // Register Windows-specific services
        services.AddSingleton<IPlatformUpdateInstaller, WindowsUpdateInstaller>();
        services.AddSingleton<IGameInstallationDetector, WindowsInstallationDetector>();
        services.AddSingleton<IGitHubTokenStorage, WindowsGitHubTokenStorage>();

        // Register WindowsFileOperationsService with factory to avoid circular dependency
        services.AddScoped<IFileOperationsService>(serviceProvider =>
        {
            var baseService = serviceProvider.GetRequiredService<FileOperationsService>();
            var casService = serviceProvider.GetRequiredService<ICasService>();
            var logger = serviceProvider.GetRequiredService<ILogger<WindowsFileOperationsService>>();
            return new WindowsFileOperationsService(baseService, casService, logger);
        });

        return services;
    }
}
