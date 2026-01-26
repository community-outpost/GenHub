using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// <summary>
    /// Initializes the application's main window and lifecycle when running as a classic desktop application.
    /// </summary>
    /// <remarks>
    /// When the application lifetime is a classic desktop lifetime, this method creates the main window with its
    /// view model, applies persisted window settings, assigns the window as the application's main window,
    /// registers shutdown handling, subscribes to single-instance IPC commands, and begins processing startup arguments.
    /// </remarks>
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

            // Subscribe to IPC commands from secondary instances (Windows only)
            SubscribeToSingleInstanceCommands(mainWindow);

            // Handle launch profile from startup args (first launch with shortcut)
            SafeFireAndForget(HandleLaunchProfileArgsAsync(desktop.Args, mainWindow), "HandleLaunchProfileArgsAsync");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Handles a subscription command for a given URL.
    /// </summary>
    /// <param name="url">The URL to subscribe to.</param>
    /// <summary>
    /// Handles a subscription URL by showing a confirmation dialog and, if confirmed, refreshing the downloads view.
    /// </summary>
    /// <param name="url">The subscription URL to process.</param>
    /// <returns>A task that completes when the subscription command has been processed.</returns>
    public async Task HandleSubscribeCommandAsync(string url)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();
        try
        {
            logger?.LogInformation("Processing subscription for URL: {Url}", url);

            // For now, we'll just log it.
            // Phase 5 will implement the confirmation dialog and actual subscription logic.
            // Dispatch to UI thread if we need to show a dialog
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    logger?.LogInformation("Showing subscription confirmation dialog for: {Url}", url);

                    var viewModel = ActivatorUtilities.CreateInstance<SubscriptionConfirmationViewModel>(_serviceProvider, url);
                    var dialog = new SubscriptionConfirmationDialog
                    {
                        DataContext = viewModel,
                    };

                    var result = await dialog.ShowDialog<bool>(desktop.MainWindow);
                    if (result)
                    {
                        logger?.LogInformation("User confirmed subscription for: {Url}", url);

                        // Optional: Trigger a refresh of the publishers list in DownloadsBrowserViewModel if it's active
                        var mainVm = desktop.MainWindow.DataContext as MainViewModel;
                        if (mainVm?.DownloadsViewModel is { } downloadsVm)
                        {
                            await downloadsVm.InitializeAsync();
                        }
                    }
                    else
                    {
                        logger?.LogInformation("User cancelled subscription for: {Url}", url);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to handle subscription command for {Url}", url);
        }
    }

    /// <summary>
    /// Update the main view model to mark the specified profile as running and set a launch status message.
    /// </summary>
    /// <param name="mainWindow">The application's main window whose DataContext is the <see cref="MainViewModel"/>.</param>
    /// <param name="profileId">The identifier of the profile to update; matched case-insensitively against profile IDs.</param>
    /// <param name="processId">The operating system process ID of the launched profile.</param>
    private static void UpdateViewModelAfterLaunch(MainWindow mainWindow, string profileId, int processId)
    {
        var mainViewModel = mainWindow.DataContext as MainViewModel;
        if (mainViewModel?.GameProfilesViewModel == null)
        {
            return;
        }

        var targetProfile = mainViewModel.GameProfilesViewModel.Profiles
            .FirstOrDefault(p => p.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));

        if (targetProfile != null)
        {
            targetProfile.IsProcessRunning = true;
            targetProfile.ProcessId = processId;
        }

        mainViewModel.GameProfilesViewModel.StatusMessage = $"Profile launched (Process ID: {processId})";
    }

    private static void UpdateViewModelWithError(MainWindow mainWindow, string error)
    {
        var mainViewModel = mainWindow.DataContext as MainViewModel;
        if (mainViewModel?.GameProfilesViewModel != null)
        {
            mainViewModel.GameProfilesViewModel.StatusMessage = $"Launch failed: {error}";
            mainViewModel.GameProfilesViewModel.ErrorMessage = error;
        }
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

    /// <summary>
    /// Processes startup command-line arguments to launch a game profile or handle a subscription URL.
    /// </summary>
    /// <param name="args">Command-line arguments to inspect for a profile ID or subscription URL.</param>
    /// <param name="mainWindow">The main application window used to update UI state when launching a profile.</param>
    /// <returns>A task that completes after any detected launch or subscription arguments have been processed.</returns>
    private async Task HandleLaunchProfileArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (args == null || args.Length == 0)
        {
            return;
        }

        var profileId = CommandLineParser.ExtractProfileId(args);
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogInformation("Startup launch detected for profile: {ProfileId}", profileId);
            await LaunchProfileByIdAsync(profileId, mainWindow);
        }

        var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
        if (!string.IsNullOrWhiteSpace(subscriptionUrl))
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogInformation("Startup subscription detected: {Url}", subscriptionUrl);
            await HandleSubscribeCommandAsync(subscriptionUrl);
        }
    }

    /// <summary>
    /// Subscribes to single-instance IPC commands and dispatches received commands to the UI thread for handling.
    /// </summary>
    /// <param name="mainWindow">The application's main window used as the target/context when handling incoming commands.</param>
    private void SubscribeToSingleInstanceCommands(MainWindow mainWindow)
    {
        // Get the SingleInstanceManager from AppLocator (set by Windows Program.cs)
        var singleInstanceManager = AppLocator.SingleInstanceManager;
        if (singleInstanceManager is null)
        {
            return;
        }

        singleInstanceManager.CommandReceived += (_, command) =>
        {
            // Dispatch to UI thread since the event comes from a background pipe listener
            Dispatcher.UIThread.Post(() => HandleSingleInstanceCommand(command, mainWindow));
        };

        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogDebug("Subscribed to single instance IPC commands");
    }

    /// <summary>
    /// Dispatches a single-instance IPC command to the appropriate handler.
    /// </summary>
    /// <param name="command">IPC command string. Recognized prefixes: <c>IpcCommands.LaunchProfilePrefix</c> to launch a profile and <c>IpcCommands.SubscribePrefix</c> to process a subscription URL.</param>
    /// <param name="mainWindow">The main application window used for UI updates when handling the command.</param>
    private void HandleSingleInstanceCommand(string command, MainWindow mainWindow)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();

        if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var profileId = command[IpcCommands.LaunchProfilePrefix.Length..];
            logger?.LogInformation("Received IPC launch command for profile: {ProfileId}", profileId);

            // Launch the profile
            SafeFireAndForget(LaunchProfileByIdAsync(profileId, mainWindow), "LaunchProfileByIdAsync");
        }
        else if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var url = command[IpcCommands.SubscribePrefix.Length..];
            logger?.LogInformation("Received IPC subscribe command for URL: {Url}", url);

            // Handle subscription
            SafeFireAndForget(HandleSubscribeCommandAsync(url), "HandleSubscribeCommandAsync");
        }
        else
        {
            logger?.LogWarning("Unknown IPC command received: {Command}", command);
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
}