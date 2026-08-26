using System;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for the Replay Manager tool and CRC mapping infrastructure.
/// </summary>
public static class ReplayManagerModule
{
    /// <summary>
    /// Adds Replay Manager services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddReplayManagerServices(this IServiceCollection services)
    {
        // Register HttpClient for UrlParserService with proper headers
        services.AddHttpClient<UrlParserService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", ApiConstants.BrowserUserAgent);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddTransient<IUrlParserService>(sp => sp.GetRequiredService<UrlParserService>());

        // Register HttpClient for CrcCatalogUpdateService
        services.AddHttpClient<CrcCatalogUpdateService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", ApiConstants.BrowserUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // CRC Mapping Registry & Header Parser
        services.AddSingleton<ICrcMappingRegistry, CrcMappingRegistry>();
        services.AddSingleton<IReplayHeaderParser, ReplayHeaderParser>();

        // Hosted background catalog update service
        services.AddHostedService<CrcCatalogUpdateService>(sp => sp.GetRequiredService<CrcCatalogUpdateService>());

        // Services
        services.AddSingleton<IReplayDirectoryService, ReplayDirectoryService>();
        services.AddSingleton<IReplayImportService, ReplayImportService>();
        services.AddSingleton<IReplayExportService, ReplayExportService>();
        services.AddSingleton<IZipValidationService, ZipValidationService>();

        // ViewModel (Singleton to persist state across tool activations)
        services.AddSingleton<ReplayManagerViewModel>();

        // Tool Plugin (Registered as a singleton IToolPlugin)
        services.AddSingleton<IToolPlugin, ReplayManagerToolPlugin>();

        return services;
    }
}
