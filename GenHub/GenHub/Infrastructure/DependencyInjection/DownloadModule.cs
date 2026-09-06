using System;
using System.Net.Http;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering download-related services.
/// </summary>
public static class DownloadModule
{
    /// <summary>
    /// Registers download services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDownloadServices(this IServiceCollection services)
    {
        // DownloadService is shared by singleton content deliverers and manifest factories.
        // Its dependencies are singleton-safe, and its HttpClient is supplied by the factory.
        services.AddSingleton<DownloadService>(serviceProvider => new DownloadService(
            serviceProvider.GetService<ILogger<DownloadService>>() ?? NullLogger<DownloadService>.Instance,
            serviceProvider.GetRequiredService<HttpClient>(),
            serviceProvider.GetRequiredService<IFileHashProvider>()));
        services.AddSingleton<IDownloadService>(serviceProvider => serviceProvider.GetRequiredService<DownloadService>());

        // Note: IContentStateService is registered as Singleton in ContentPipelineModule.AddSharedComponents
        // to ensure a single instance with consistent state change events.

        // Register HttpClient with configuration from IConfigurationProviderService
        services.AddSingleton<HttpClient>(serviceProvider =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            var client = new HttpClient();

            var userAgent = configProvider.GetDownloadUserAgent();
            var timeoutSeconds = configProvider.GetDownloadTimeoutSeconds();

            client.DefaultRequestHeaders.Add("User-Agent", userAgent ?? ApiConstants.DefaultUserAgent);
            client.Timeout = timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeIntervals.DownloadTimeout;

            return client;
        });

        return services;
    }
}