using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenHub.Common.Services;
using GenHub.Common.ViewModels;
using GenHub.Common.Views;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Features.Downloads.Views;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Infrastructure.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace GenHub;

/// <summary>
/// Primary application class for GenHub.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets or sets the delegate used to display the subscription confirmation dialog.
    /// Can be overridden in integration tests to simulate user actions headlessly.
    /// </summary>
    internal Func<SubscriptionConfirmationViewModel, Window?, Task<bool>> ShowSubscriptionDialogAsync { get; set; } =
        async (vm, owner) =>
        {
            var dialog = new SubscriptionConfirmationDialog
            {
                DataContext = vm,
            };

            if (owner != null)
            {
                return await dialog.ShowDialog<bool>(owner);
            }

            var tcs = new TaskCompletionSource<bool>();
            dialog.Closed += (_, _) => tcs.TrySetResult(dialog.DialogResult);
            dialog.Show();
            return await tcs.Task;
        };

    private readonly IServiceProvider _serviceProvider;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly ILocalizationService _localizationService;
    private readonly IProfileLauncherFacade _profileLauncherFacade;
    private readonly IThemeService? _themeService;
    private bool _startupArgsHandled;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class with the specified service provider.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider for dependency injection.</param>
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _userSettingsService = _serviceProvider.GetService<IUserSettingsService>() ?? throw new InvalidOperationException("IUserSettingsService not registered");
        _configurationProvider = _serviceProvider.GetService<IConfigurationProviderService>() ?? throw new InvalidOperationException("IConfigurationProviderService not registered");
        _localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
        _profileLauncherFacade = _serviceProvider.GetRequiredService<IProfileLauncherFacade>();
        _themeService = _serviceProvider.GetService<IThemeService>();
    }

    /// <summary>
    /// Initializes the Avalonia application and loads XAML resources.
    /// </summary>
    public override void Initialize()
    {
        try
        {
            var configuredLanguage = _userSettingsService.Get()?.Language;
            if (!string.IsNullOrWhiteSpace(configuredLanguage))
            {
                try
                {
                    var result = _localizationService.SetCulture(new CultureInfo(configuredLanguage));
                    if (!result.Success)
                    {
                        var logger = _serviceProvider?.GetService<ILogger<App>>();
                        logger?.LogWarning(
                            "Failed to apply configured language '{Language}': {Errors}; falling back to default",
                            configuredLanguage,
                            string.Join(", ", result.Errors));
                    }
                }
                catch (CultureNotFoundException ex)
                {
                    var logger = _serviceProvider?.GetService<ILogger<App>>();
                    logger?.LogWarning(ex, "Configured language '{Language}' was not recognized; falling back to default", configuredLanguage);
                }
            }
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogWarning(ex, "Failed to load or apply configured language; falling back to default");
        }

        // Make localization available while application XAML resources are loading.
        Resources[LocalizationConstants.ResourceServiceKey] = _localizationService;
        AvaloniaXamlLoader.Load(this);

        // App XAML replaces the resource dictionary, so restore the service for views loaded afterward.
        Resources[LocalizationConstants.ResourceServiceKey] = _localizationService;
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

            // Subscribe to IPC commands from secondary instances (Windows and Linux)
            SubscribeToSingleInstanceCommands(mainWindow);

            // Handle startup arguments sequentially once the window is opened and active
            mainWindow.Opened += (_, _) =>
                SafeFireAndForget(HandleStartupArgsAsync(desktop.Args, mainWindow), nameof(HandleStartupArgsAsync));

            // Repair desktop and application shortcuts if application executable has moved/relocated
            SafeFireAndForget(RepairShortcutsAsync(), nameof(RepairShortcutsAsync));

            // Detect and resolve duplicate installation collisions across platforms
            var conflictService = _serviceProvider.GetService<IInstallationConflictService>();
            if (conflictService != null)
            {
                SafeFireAndForget(conflictService.CheckAndResolveConflictsAsync(), nameof(IInstallationConflictService.CheckAndResolveConflictsAsync));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Handles a catalog subscription URL by prompting the user for confirmation.
    /// </summary>
    /// <param name="subscriptionUrl">The subscription URL to handle.</param>
    /// <param name="mainWindow">The optional main window for modal presentation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal async Task HandleSubscriptionUrlAsync(string subscriptionUrl, MainWindow? mainWindow = null)
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();

        try
        {
            if (string.IsNullOrWhiteSpace(subscriptionUrl))
            {
                return;
            }

            var targetUrl = ResolveTargetSubscriptionUrl(subscriptionUrl);
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                logger?.LogWarning("Invalid or unsafe subscription URL: {Url}", subscriptionUrl);
                return;
            }

            logger?.LogInformation("Handling subscription URL: {Url}", targetUrl);

            var subscriptionStore = _serviceProvider.GetService<IPublisherSubscriptionStore>();
            var catalogParser = _serviceProvider.GetService<IPublisherCatalogParser>();
            var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();

            if (subscriptionStore == null || catalogParser == null || httpClientFactory == null || loggerFactory == null)
            {
                logger?.LogError("Required services for catalog subscription are not registered");
                return;
            }

            var vmLogger = loggerFactory.CreateLogger<SubscriptionConfirmationViewModel>();
            var confirmationVm = new SubscriptionConfirmationViewModel(
                targetUrl,
                subscriptionStore,
                catalogParser,
                httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName),
                vmLogger,
                _localizationService);

            var confirmed = await ShowSubscriptionDialogAsync(confirmationVm, mainWindow);
            if (confirmed)
            {
                await HandleConfirmedSubscriptionAsync(mainWindow, targetUrl, logger);
            }
            else
            {
                logger?.LogInformation("Subscription was cancelled or not confirmed for URL: {Url}", targetUrl);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception while handling subscription URL {Url}", subscriptionUrl);
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

    private static async Task RepairProfileShortcutsAsync(
        IShortcutService shortcutService,
        IGameProfileManager profileManager,
        ILogger<App>? logger)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync();
        if (!profilesResult.Success || profilesResult.Data == null)
        {
            return;
        }

        foreach (var profile in profilesResult.Data)
        {
            if (await shortcutService.ShortcutExistsAsync(profile))
            {
                var result = await shortcutService.CreateDesktopShortcutAsync(profile);
                if (!result.Success)
                {
                    logger?.LogWarning("Failed to repair desktop shortcut for profile {ProfileName}: {Error}", profile.Name, result.FirstError);
                }
            }
        }
    }

    private static string? ResolveTargetSubscriptionUrl(string subscriptionUrl)
    {
        var targetUrl = CommandLineParser.ExtractSubscriptionUrl([subscriptionUrl]);
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            return targetUrl;
        }

        var sanitized = subscriptionUrl.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim('"', '\'', ' ', '\t');
        if (Uri.TryCreate(sanitized, UriKind.Absolute, out var directUri) && IsAllowedSubscriptionScheme(directUri))
        {
            return sanitized;
        }

        return null;
    }

    private static bool IsAllowedSubscriptionScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.IsFile && !uri.IsUnc && string.IsNullOrEmpty(uri.Host));
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
            logger?.LogError(ex, "Error during application shutdown");
        }
        finally
        {
            try
            {
                if (_serviceProvider is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
                else if (_serviceProvider is IDisposable syncDisposable)
                {
                    syncDisposable.Dispose();
                }
            }
            catch (Exception disposeEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing service provider during shutdown: {disposeEx.Message}");
            }
        }
    }

    private async Task HandleStartupArgsAsync(string[]? args, MainWindow mainWindow)
    {
        if (_startupArgsHandled || args == null || args.Length == 0)
        {
            return;
        }

        _startupArgsHandled = true;
        await HandleLaunchProfileArgsAsync(args, mainWindow);
        await HandleSubscriptionArgsAsync(args, mainWindow);
        await HandleImportProfileArgsAsync(args, mainWindow);
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
        logger?.LogInformation("Startup profile launch request for ID: {ProfileId}", profileId);

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
        logger?.LogInformation("Startup subscription request for URL: {Url}", subscriptionUrl);

        await HandleSubscriptionUrlAsync(subscriptionUrl, mainWindow);
    }

    private void SubscribeToSingleInstanceCommands(MainWindow mainWindow)
    {
        var commandReceiver = AppLocator.SingleInstanceManager;
        if (commandReceiver == null)
        {
            return;
        }

        commandReceiver.CommandReceived += (_, command) =>
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

            // Handle the profile launch
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
        else if (string.Equals(command, IpcCommands.ActivateCommand, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation("Received IPC activate command");
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
        }
        else
        {
            logger?.LogWarning("Unknown IPC command received: {Command}", command);
        }
    }

    private async Task HandleImportProfileUriAsync(string shareUriOrPath, MainWindow mainWindow)
    {
        if (string.IsNullOrWhiteSpace(shareUriOrPath))
        {
            return;
        }

        var trimmed = shareUriOrPath.Trim();
        bool isValid = trimmed.StartsWith(CommandLineConstants.UriScheme, StringComparison.OrdinalIgnoreCase) ||
                       trimmed.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase);

        if (!isValid)
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogWarning("Rejected invalid profile import target: {Target}", trimmed);
            return;
        }

        var launcherViewModel = _serviceProvider.GetService<GameProfileLauncherViewModel>();
        if (launcherViewModel != null)
        {
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
            await launcherViewModel.ImportProfileFromFileOrUriAsync(trimmed);
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

    private async Task HandleConfirmedSubscriptionAsync(MainWindow? mainWindow, string targetUrl, ILogger<App>? logger)
    {
        if (mainWindow?.DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.SelectTab(NavigationTab.Downloads);
            if (mainViewModel.DownloadsBrowserViewModel != null)
            {
                await mainViewModel.DownloadsBrowserViewModel.InitializeAsync();
            }
        }

        logger?.LogInformation("User confirmed subscription to: {Url}", targetUrl);
        var notificationService = _serviceProvider.GetService<INotificationService>();
        var title = LocalizationConverterHelper.GetLocalizedOrDefault(_localizationService, "Downloads.Subscription.SubscribedNotificationTitle", "Subscribed");
        var message = LocalizationConverterHelper.GetLocalizedOrDefault(_localizationService, "Downloads.Subscription.SubscribedNotificationMessage", "Successfully subscribed to content catalog.");
        notificationService?.ShowSuccess(title, message);
    }

    private async Task RepairShortcutsAsync()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        await Task.Run(ExecuteRepairShortcutsAsync);
    }

    private async Task ExecuteRepairShortcutsAsync()
    {
        var logger = _serviceProvider.GetService<ILogger<App>>();
        try
        {
            var shortcutService = _serviceProvider.GetService<IShortcutService>();
            var profileManager = _serviceProvider.GetService<IGameProfileManager>();
            if (shortcutService == null || profileManager == null)
            {
                return;
            }

            await RepairProfileShortcutsAsync(shortcutService, profileManager, logger);

            var repairAppResult = await shortcutService.RepairApplicationShortcutsAsync();
            if (!repairAppResult.Success)
            {
                logger?.LogWarning("Failed to repair application shortcuts: {Error}", repairAppResult.FirstError);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to repair desktop shortcuts during startup");
        }
    }
}
