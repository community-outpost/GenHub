using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Core;
using GenHub.Services;
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
            var viewModel = AppLocator.Services?.GetRequiredService<MainViewModel>()
                            ?? new MainViewModel(new GameDetectionService(new DummyGameDetector()));

            desktop.MainWindow = new MainWindow()
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}