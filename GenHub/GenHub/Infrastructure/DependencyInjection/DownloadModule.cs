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
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDownloadServices(this IServiceCollection services)
    {
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddHttpClient<DownloadService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GenHub/1.0");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        return services;
    }
}
