using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
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
                            ?? new MainViewModel();

            desktop.MainWindow = new MainWindow()
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
