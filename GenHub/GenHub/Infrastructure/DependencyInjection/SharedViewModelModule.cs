using System;
using GenHub.Common.ViewModels;
using GenHub.Core;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Registers shared ViewModels and Services for DI.
/// </summary>
public static class SharedViewModelModule
{
    /// <summary>
    /// Adds shared view model services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
    {
        // Register MainViewModel (critical for app startup)
        services.AddSingleton<MainViewModel>();

        // Register tab ViewModels
        services.AddSingleton<GameProfileLauncherViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<GameProfileSettingsViewModel>();

        // Register factory for GameProfileItemViewModel (has required constructor parameters)
        services.AddTransient<Func<string, IGameProfile, string, string, GameProfileItemViewModel>>(sp =>
            (profileId, profile, icon, cover) => new GameProfileItemViewModel(profileId, profile, icon, cover));

        return services;
    }
}
