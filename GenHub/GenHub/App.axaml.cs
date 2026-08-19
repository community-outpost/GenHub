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
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
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
    /// Gets the application service provider.
    /// </summary>
    public IServiceProvider ServiceProvider => _serviceProvider;

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

            // Subscribe to IPC commands from secondary instances (Windows only)
            SubscribeToSingleInstanceCommands(mainWindow);

            // Handle startup arguments sequentially (launch profile, then subscription if present)
            SafeFireAndForget(HandleStartupArgsAsync(desktop.Args, mainWindow), nameof(HandleStartupArgsAsync));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Handles a content-subscription request for the given URL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entry points: <c>genhub://subscribe?url=...</c> on cold start, IPC from a secondary
    /// instance, or dragging a JSON file with a <c>catalogUrl</c> onto the main window.
    /// </para>
    /// <para>
    /// <b>Current target (catalog-direct):</b> <paramref name="url"/> is expected to be a
    /// hosted GenHub <c>catalog.json</c> (<see cref="GenHub.Core.Models.Providers.PublisherCatalog"/>).
    /// Confirming saves a <see cref="GenHub.Core.Models.Providers.PublisherSubscription"/> and
    /// refreshes Downloads so the publisher appears in the sidebar.
    /// </para>
    /// <para>
    /// <b>Future (Publisher Studio):</b> the same protocol may point at a Provider Definition
    /// (publisher metadata + catalog endpoint(s)). Detection/parsing of definitions will extend
    /// this path without changing the URI scheme; catalog-direct subscribe remains supported.
    /// </para>
    /// </remarks>
    /// <param name="url">
    /// Absolute HTTP(S) URL of a GenHub catalog JSON today; later also a provider-definition URL.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleSubscribeCommandAsync(string url)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();
        try
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttps &&
                 !(parsedUri.Scheme == Uri.UriSchemeHttp && parsedUri.IsLoopback)))
            {
                logger?.LogWarning("Rejected invalid or non-HTTPS subscription URL: '{Url}'", url);
                return;
            }

            logger?.LogInformation("Processing subscription for URL: {Url}", url);

            // UI work (dialog + Downloads refresh) must run on the Avalonia UI thread.
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

                        // Reload subscribed publishers into the Downloads sidebar without wiping
                        // the user's current browse/filter state for built-in publishers.
                        var mainVm = desktop.MainWindow.DataContext as MainViewModel;
                        if (mainVm?.DownloadsBrowserViewModel is { } downloadsVm)
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
    }

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

        // genhub://subscribe?url=<catalog-or-future-definition-url> on first launch
        var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
        if (!string.IsNullOrWhiteSpace(subscriptionUrl))
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogInformation("Startup subscription detected: {Url}", subscriptionUrl);
            await HandleSubscribeCommandAsync(subscriptionUrl);
        }
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
        else if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Secondary instance forwarded genhub://subscribe?... while we were already running.
            var url = command[IpcCommands.SubscribePrefix.Length..];
            logger?.LogInformation("Received IPC subscribe command for URL: {Url}", url);

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
