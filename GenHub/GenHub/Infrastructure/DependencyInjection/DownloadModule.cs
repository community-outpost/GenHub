using System;
using GenHub.Common.Services;
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
        services.AddSingleton<IDownloadService, DownloadService>();

        var userAgent = configProvider.GetDownloadUserAgent();
        var timeout = configProvider.GetDownloadTimeoutSeconds();

        services.AddHttpClient<DownloadService>(client =>
        {
            client.DefaultRequestHeaders.Remove("User-Agent");
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        return services;
    }
}
