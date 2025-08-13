using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace GenHub;

/// <summary>
/// Primary application class for GenHub.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IConfigurationProviderService _configurationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class with the specified service provider.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider for dependency injection.</param>
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _userSettingsService = _serviceProvider.GetService<IUserSettingsService>() ?? throw new InvalidOperationException("IUserSettingsService not registered");
        _configurationProvider = _serviceProvider.GetService<IConfigurationProviderService>() ?? throw new InvalidOperationException("IConfigurationProviderService not registered");
    }

    /// <summary>
    /// Initializes the Avalonia application and loads XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the Avalonia framework initialization is completed.
    /// Sets up the main window and applies window settings.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetService<MainViewModel>(),
            };
            ApplyWindowSettings(mainWindow);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyWindowSettings(MainWindow mainWindow)
    {
        if (_configurationProvider == null) return;
        try
        {
            // Use configuration provider which properly handles defaults
            mainWindow.Width = _configurationProvider.GetWindowWidth();
            mainWindow.Height = _configurationProvider.GetWindowHeight();
            if (_configurationProvider.GetIsWindowMaximized())
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            if (logger != null)
            {
                logger.LogError(ex, "Failed to apply window settings");
            }
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_userSettingsService == null || _serviceProvider == null) return;
        try
        {
            // Save current window state
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                _userSettingsService.UpdateSettings(settings =>
                {
                    if (desktop.MainWindow.WindowState != Avalonia.Controls.WindowState.Maximized)
                    {
                        settings.WindowWidth = desktop.MainWindow.Width;
                        settings.WindowHeight = desktop.MainWindow.Height;
                    }

                    settings.IsMaximized = desktop.MainWindow.WindowState == Avalonia.Controls.WindowState.Maximized;
                });
                await _userSettingsService.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            if (logger != null)
            {
                logger.LogError(ex, "Failed to save settings on shutdown");
            }
        }
        finally
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
