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
using GenHub.Core.Models.GameProfiles;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering shared ViewModels in the dependency injection container.
/// </summary>
public static class SharedViewModelModule
{
    /// <summary>
    /// Registers shared ViewModels and their dependencies with the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
    {
        // Register ViewModels as transient (unless singleton is explicitly required)
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<GameProfileLauncherViewModel>();
        services.AddSingleton<DownloadsBrowserViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<InfoViewModel>();
        services.AddSingleton<NotificationManagerViewModel>();
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
            sp.GetRequiredService<IDialogService>(),
            sp.GetService<IThemeService>(),
            sp.GetService<IGitHubTokenStorage>(),
            sp.GetService<IGitHubApiClient>()));
        services.AddSingleton<GameProfileSettingsViewModel>();

        // Register ProfileSelectionViewModel as transient for profile selection scenarios
        services.AddTransient<ProfileSelectionViewModel>();

        // Register factory for GameProfileItemViewModel (has required constructor parameters)
        services.AddTransient<Func<string, IGameProfile, string, string, GameProfileItemViewModel>>(sp =>
            (profileId, profile, icon, cover) => new GameProfileItemViewModel(profileId, profile, icon, cover));

        return services;
    }
}
