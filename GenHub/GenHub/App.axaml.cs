using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Features.GameInstallations;
using GenHub.ViewModels;
using GenHub.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub;

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
            var orchestrator = AppLocator.Services?.GetService<GameInstallationDetectionOrchestrator>()
                                ?? new GameInstallationDetectionOrchestrator([]);
            var viewModel = AppLocator.Services?.GetService<MainViewModel>()
                            ?? new MainViewModel(orchestrator);

            desktop.MainWindow = new MainWindow()
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
