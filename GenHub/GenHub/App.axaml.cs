using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub;

/// <summary>
/// Primary application class for GenHub.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IProfileLauncherFacade _profileLauncherFacade;
    private readonly IThemeService? _themeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class with the specified service provider.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider for dependency injection.</param>
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _userSettingsService = _serviceProvider.GetService<IUserSettingsService>() ?? throw new InvalidOperationException("IUserSettingsService not registered");
        _configurationProvider = _serviceProvider.GetService<IConfigurationProviderService>() ?? throw new InvalidOperationException("IConfigurationProviderService not registered");
        _profileLauncherFacade = _serviceProvider.GetRequiredService<IProfileLauncherFacade>();
        _themeService = _serviceProvider.GetService<IThemeService>();
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
        _themeService?.InitializeTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetService<MainViewModel>(),
            };
            ApplyWindowSettings(mainWindow);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;

            // Subscribe to IPC commands from secondary instances (Windows only)
            SubscribeToSingleInstanceCommands(mainWindow);

            // Handle startup arguments sequentially once the window is opened and active
            mainWindow.Opened += (_, _) =>
                SafeFireAndForget(HandleStartupArgsAsync(desktop.Args, mainWindow), nameof(HandleStartupArgsAsync));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void UpdateViewModelAfterLaunch(MainWindow mainWindow, string profileId, int processId)
    {
        if (mainWindow?.DataContext is not MainViewModel mainViewModel || mainViewModel.GameProfilesViewModel == null)
        {
            return;
        }

        if (mainViewModel.GameProfilesViewModel.Profiles != null)
        {
            var targetProfile = mainViewModel.GameProfilesViewModel.Profiles
                .FirstOrDefault(p => p.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));

            if (targetProfile != null)
            {
                targetProfile.IsProcessRunning = true;
                targetProfile.ProcessId = processId;
            }
        }

        mainViewModel.GameProfilesViewModel.StatusMessage = $"Profile launched (Process ID: {processId})";
    }

    private static void UpdateViewModelWithError(MainWindow mainWindow, string error)
    {
        if (mainWindow?.DataContext is not MainViewModel mainViewModel || mainViewModel.GameProfilesViewModel == null)
        {
            return;
        }

        mainViewModel.GameProfilesViewModel.StatusMessage = $"Launch failed: {error}";
        mainViewModel.GameProfilesViewModel.ErrorMessage = error;
    }

    private void ApplyWindowSettings(MainWindow mainWindow)
    {
        if (_configurationProvider == null)
        {
            return;
        }

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
            logger?.LogError(ex, "Failed to apply window settings");
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_serviceProvider == null)
        {
            return;
        }

        try
        {
            // Save current window state
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                _userSettingsService.Update(settings =>
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
            logger?.LogError(ex, "Failed to save settings on shutdown");
        }
        finally
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task HandleStartupArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (args == null || args.Length == 0)
        {
            return;
        }

        await HandleLaunchProfileArgsAsync(args, mainWindow);
        await HandleSubscriptionArgsAsync(args, mainWindow);
        await HandleImportProfileArgsAsync(args, mainWindow);
    }

    private async Task HandleLaunchProfileArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (args == null || args.Length == 0)
        {
            return;
        }

        var profileId = CommandLineParser.ExtractProfileId(args);
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Startup launch detected for profile: {ProfileId}", profileId);

        await LaunchProfileByIdAsync(profileId, mainWindow);
    }

    private async Task HandleSubscriptionArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (args == null || args.Length == 0)
        {
            return;
        }

        var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
        if (string.IsNullOrWhiteSpace(subscriptionUrl))
        {
            return;
        }

        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Startup subscription detected for URL: {Url}", subscriptionUrl);

        await HandleSubscriptionUrlAsync(subscriptionUrl, mainWindow);
    }

    private async Task HandleImportProfileArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (args == null || args.Length == 0)
        {
            return;
        }

        var shareUri = CommandLineParser.ExtractProfileShareUri(args);
        if (string.IsNullOrWhiteSpace(shareUri))
        {
            return;
        }

        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Startup profile import request received");

        await HandleImportProfileUriAsync(shareUri, mainWindow);
    }

    private void SubscribeToSingleInstanceCommands(MainWindow mainWindow)
    {
        // Get the SingleInstanceManager from AppLocator (set by Windows Program.cs)
        var singleInstanceManager = AppLocator.SingleInstanceManager;
        if (singleInstanceManager is null)
        {
            return;
        }

        singleInstanceManager.CommandReceived += (_, command) =>
            Dispatcher.UIThread.Post(() => HandleSingleInstanceCommand(command, mainWindow));

        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogDebug("Subscribed to single instance IPC commands");
    }

    private void HandleSingleInstanceCommand(string command, MainWindow mainWindow)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();

        if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var profileId = command[IpcCommands.LaunchProfilePrefix.Length..];
            logger?.LogInformation("Received IPC launch command for profile: {ProfileId}", profileId);

            // Launch the profile
            SafeFireAndForget(LaunchProfileByIdAsync(profileId, mainWindow), nameof(LaunchProfileByIdAsync));
        }
        else if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var subscriptionUrl = command[IpcCommands.SubscribePrefix.Length..];
            logger?.LogInformation("Received IPC subscribe command for URL: {Url}", subscriptionUrl);

            // Handle the subscription URL
            SafeFireAndForget(HandleSubscriptionUrlAsync(subscriptionUrl, mainWindow), nameof(HandleSubscriptionUrlAsync));
        }
        else if (command.StartsWith(IpcCommands.ImportProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var shareUri = command[IpcCommands.ImportProfilePrefix.Length..];
            logger?.LogInformation("Received IPC profile import command");

            // Handle profile import
            SafeFireAndForget(HandleImportProfileUriAsync(shareUri, mainWindow), nameof(HandleImportProfileUriAsync));
        }
        else
        {
            logger?.LogWarning("Unknown IPC command received: {Command}", command);
        }
    }

    private async Task HandleImportProfileUriAsync(string shareUriOrPath, MainWindow mainWindow)
    {
        var launcherViewModel = _serviceProvider.GetService<GameProfileLauncherViewModel>();
        if (launcherViewModel != null)
        {
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
            await launcherViewModel.ImportProfileFromFileOrUriAsync(shareUriOrPath);
        }
        else
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError("GameProfileLauncherViewModel is not available for import.");
        }
    }

    private void SafeFireAndForget(Task task, string context)
    {
        _ = task.ContinueWith(
            t =>
            {
                var logger = _serviceProvider.GetService<ILogger<App>>();
                if (t.Exception != null)
                {
                    logger?.LogError(t.Exception, "Error in {Context}", context);
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task LaunchProfileByIdAsync(string profileId, MainWindow mainWindow)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();

        try
        {
            logger?.LogInformation("Launching profile {ProfileId}...", profileId);

            var launchResult = await _profileLauncherFacade.LaunchProfileAsync(profileId);

            if (launchResult.Success && launchResult.Data != null)
            {
                logger?.LogInformation(
                    "Profile {ProfileId} launched successfully. Process ID: {ProcessId}",
                    profileId,
                    launchResult.Data.ProcessInfo.ProcessId);

                UpdateViewModelAfterLaunch(mainWindow, profileId, launchResult.Data.ProcessInfo.ProcessId);
            }
            else
            {
                var errors = string.Join(", ", launchResult.Errors);
                logger?.LogError("Failed to launch profile {ProfileId}: {Errors}", profileId, errors);
                UpdateViewModelWithError(mainWindow, errors);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception while launching profile {ProfileId}", profileId);
        }
    }

    private async Task HandleSubscriptionUrlAsync(string subscriptionUrl, MainWindow mainWindow)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();

        try
        {
            var sanitizedUrl = subscriptionUrl.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim('"', '\'', ' ', '\t');
            if (!Uri.TryCreate(sanitizedUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                logger?.LogWarning("Invalid or unsafe subscription URL: {Url}", subscriptionUrl);
                return;
            }

            logger?.LogInformation("Handling subscription URL: {Url}", uri.AbsoluteUri);

            var dialogService = _serviceProvider.GetService<IDialogService>();
            if (dialogService != null)
            {
                var confirmed = await dialogService.ShowConfirmationAsync(
                    "Subscribe to Catalog",
                    $"Do you want to subscribe to content from:\n{uri.AbsoluteUri}",
                    "Subscribe",
                    "Cancel");

                if (confirmed)
                {
                    if (mainWindow?.DataContext is MainViewModel mainViewModel)
                    {
                        mainViewModel.SelectTab(NavigationTab.Downloads);
                    }

                    logger?.LogInformation("User confirmed subscription to: {Url}", uri.AbsoluteUri);
                    var notificationService = _serviceProvider.GetService<INotificationService>();
                    notificationService?.ShowSuccess("Subscribed", $"Successfully subscribed to: {uri.AbsoluteUri}");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception while handling subscription URL {Url}", subscriptionUrl);
        }
    }
}
