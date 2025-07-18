using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameInstallations;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace GenHub;

/// <summary>
/// Primary application class for GenHub.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = AppLocator.Services?.GetService<MainViewModel>();
            if (viewModel is null)
            {
                // Create default ViewModels if DI is not configured
                var gameProfilesVM = AppLocator.Services?.GetService<GameProfileLauncherViewModel>() ?? new GameProfileLauncherViewModel();
                var downloadsVM = AppLocator.Services?.GetService<DownloadsViewModel>() ?? new DownloadsViewModel();
                var settingsVM = AppLocator.Services?.GetService<SettingsViewModel>() ?? new SettingsViewModel();

                // Resolve game installation orchestrator
                var loggerFactory = AppLocator.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => { });
                var orchestratorLogger = loggerFactory.CreateLogger<GameInstallationDetectionOrchestrator>();
                var mainViewModelLogger = loggerFactory.CreateLogger<MainViewModel>();

                var orchestrator = AppLocator.Services?.GetService<IGameInstallationDetectionOrchestrator>()
                    ?? new GameInstallationDetectionOrchestrator(
                        Array.Empty<IGameInstallationDetector>(),
                        orchestratorLogger);

                viewModel = new MainViewModel(gameProfilesVM, downloadsVM, settingsVM, orchestrator, mainViewModelLogger);
            }

            desktop.MainWindow = new MainWindow()
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
