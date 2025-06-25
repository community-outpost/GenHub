using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Helpers;
using GenHub.Features.GameVersions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

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
