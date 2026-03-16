using System;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for the Replay Manager tool.
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
        // This also registers UrlParserService as a transient service with the typed HttpClient
        services.AddHttpClient<UrlParserService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", ApiConstants.BrowserUserAgent);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Bind interface to the typed-client registration so the browser User-Agent is preserved.
        // A plain AddTransient<IUrlParserService, UrlParserService> would bypass the typed client
        // and inject the default, unconfigured HttpClient instead.
        services.AddTransient<IUrlParserService>(sp => sp.GetRequiredService<UrlParserService>());

        // Services
        services.AddSingleton<IReplayDirectoryService, ReplayDirectoryService>();
        services.AddSingleton<IReplayImportService, ReplayImportService>();
        services.AddSingleton<IReplayExportService, ReplayExportService>();
        services.AddSingleton<GenHub.Core.Interfaces.Common.IUploadHistoryService, GenHub.Features.Tools.Services.UploadHistoryService>();
        services.AddSingleton<IZipValidationService, ZipValidationService>();

        // ViewModel (Singleton to persist state across tool activations)
        services.AddSingleton<ReplayManagerViewModel>();

        // Tool Plugin (Registered as a singleton IToolPlugin)
        services.AddSingleton<IToolPlugin, ReplayManagerToolPlugin>();

        return services;
    }
}
