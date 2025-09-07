using System;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;

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
    /// <param name="configProvider">The configuration provider for HTTP client setup.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDownloadServices(
        this IServiceCollection services,
        IConfigurationProviderService configProvider)
    {
        // Register DownloadService and its interface
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<DownloadService>();

        // Register HttpClient with configuration from IConfigurationProviderService
        services.AddHttpClient<DownloadService>((serviceProvider, client) =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();

            var userAgent = configProvider.GetDownloadUserAgent();
            var timeoutSeconds = configProvider.GetDownloadTimeoutSeconds();

            client.DefaultRequestHeaders.Add("User-Agent", userAgent ?? ApiConstants.DefaultUserAgent);
            client.Timeout = timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeIntervals.DownloadTimeout;
        });

        return services;
    }
}
