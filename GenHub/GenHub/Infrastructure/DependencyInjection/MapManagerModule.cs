using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Features.Tools.MapManager;
using GenHub.Features.Tools.MapManager.Services;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.Services;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for Map Manager.
/// </summary>
public static class MapManagerModule
{
    /// <summary>
    /// Registers Map Manager services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMapManager(this IServiceCollection services)
    {
        // Services
        services.AddSingleton<IMapDirectoryService, MapDirectoryService>();
        services.AddSingleton<IMapImportService, MapImportService>();
        services.AddSingleton<IMapExportService, MapExportService>();
        services.AddScoped<IMapPackService, MapPackService>();
        services.AddSingleton<IUploadHistoryService, UploadHistoryService>();
        services.AddSingleton<MapNameParser>();
        services.AddSingleton<TgaImageParser>();
        services.AddSingleton<TgaParser>();

        // ViewModels
        services.AddTransient<MapManagerViewModel>();

        // Tool Plugin
        services.AddSingleton<IToolPlugin, MapManagerToolPlugin>();

        return services;
    }
}
