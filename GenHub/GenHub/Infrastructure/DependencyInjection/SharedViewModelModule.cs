using System;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IUserSettingsService>(),
            sp.GetRequiredService<ILogger<SettingsViewModel>>(),
            sp.GetRequiredService<ICasService>(),
            sp.GetRequiredService<IGameProfileManager>(),
            sp.GetRequiredService<IWorkspaceManager>(),
            sp.GetRequiredService<IContentManifestPool>(),
            sp.GetRequiredService<IVelopackUpdateManager>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<IConfigurationProviderService>(),
            sp.GetRequiredService<IGameInstallationService>(),
            sp.GetRequiredService<IStorageLocationService>(),
            sp.GetRequiredService<IUserDataTracker>(),
            sp.GetRequiredService<IGitHubTokenStorage>()));
        services.AddSingleton<GameProfileSettingsViewModel>();

        // Register PublisherCardViewModel as transient
        services.AddTransient<PublisherCardViewModel>();

        // Register NotificationFeedViewModel
        services.AddSingleton<NotificationFeedViewModel>();

        // Register factory for GameProfileItemViewModel (has required constructor parameters)
        services.AddTransient<Func<string, IGameProfile, string, string, GameProfileItemViewModel>>(sp =>
            (profileId, profile, icon, cover) => new GameProfileItemViewModel(profileId, profile, icon, cover));

        return services;
    }
}
