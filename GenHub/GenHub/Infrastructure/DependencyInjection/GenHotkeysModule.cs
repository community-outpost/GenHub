using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for GenHotkeys Visual Hotkey Editor.
/// </summary>
public static class GenHotkeysModule
{
    /// <summary>
    /// Registers GenHotkeys services, viewmodels, and tool plugin.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenHotkeys(this IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ITechTreeService, TechTreeService>();
        services.AddSingleton<IHotkeyProfileStorageService, HotkeyProfileStorageService>();
        services.AddSingleton<IIconOverlayService, IconOverlayService>();
        services.AddSingleton<IHotkeyPackageService, HotkeyPackageService>();

        // ViewModel
        services.AddTransient<GenHotkeysViewModel>();

        // Tool Plugin
        services.AddSingleton<IToolPlugin, GenHotkeysToolPlugin>();

        return services;
    }
}
