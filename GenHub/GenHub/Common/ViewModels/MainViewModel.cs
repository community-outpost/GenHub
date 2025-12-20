using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<MainViewModel>? _logger;
    private readonly IGameInstallationDetectionOrchestrator _gameInstallationDetectionOrchestrator;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IProfileEditorFacade _profileEditorFacade;
    private readonly IVelopackUpdateManager _velopackUpdateManager;
    private readonly CancellationTokenSource _initializationCts = new();

    [ObservableProperty]
    private NavigationTab _selectedTab = NavigationTab.GameProfiles;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="gameProfilesViewModel">Game profiles view model.</param>
    /// <param name="downloadsViewModel">Downloads view model.</param>
    /// <param name="toolsViewModel">Tools view model.</param>
    /// <param name="settingsViewModel">Settings view model.</param>
    /// <param name="notificationManager">Notification manager view model.</param>
    /// <param name="gameInstallationDetectionOrchestrator">Game installation orchestrator.</param>
    /// <param name="configurationProvider">Configuration provider service.</param>
    /// <param name="userSettingsService">User settings service for persistence operations.</param>
    /// <param name="profileEditorFacade">Profile editor facade for automatic profile creation.</param>
    /// <param name="velopackUpdateManager">The Velopack update manager for checking updates.</param>
    /// <param name="logger">Logger instance.</param>
    public MainViewModel(
        GameProfileLauncherViewModel gameProfilesViewModel,
        DownloadsViewModel downloadsViewModel,
        ToolsViewModel toolsViewModel,
        SettingsViewModel settingsViewModel,
        NotificationManagerViewModel notificationManager,
        IGameInstallationDetectionOrchestrator gameInstallationDetectionOrchestrator,
        IConfigurationProviderService configurationProvider,
        IUserSettingsService userSettingsService,
        IProfileEditorFacade profileEditorFacade,
        IVelopackUpdateManager velopackUpdateManager,
        ILogger<MainViewModel>? logger = null)
    {
        GameProfilesViewModel = gameProfilesViewModel;
        DownloadsViewModel = downloadsViewModel;
        ToolsViewModel = toolsViewModel;
        SettingsViewModel = settingsViewModel;
        NotificationManager = notificationManager;
        _gameInstallationDetectionOrchestrator = gameInstallationDetectionOrchestrator;
        _configurationProvider = configurationProvider;
        _userSettingsService = userSettingsService;
        _profileEditorFacade = profileEditorFacade ?? throw new ArgumentNullException(nameof(profileEditorFacade));
        _velopackUpdateManager = velopackUpdateManager ?? throw new ArgumentNullException(nameof(velopackUpdateManager));
        _logger = logger;

        // Load initial settings using unified configuration
        try
        {
            _selectedTab = _configurationProvider.GetLastSelectedTab();
            _logger?.LogDebug($"Initial settings loaded, selected tab: {_selectedTab}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load initial settings");
            _selectedTab = NavigationTab.GameProfiles;
        }

        // Tab change handled by ObservableProperty partial method
    }

    /// <summary>
    /// Gets the game profiles view model.
    /// </summary>
    public GameProfileLauncherViewModel GameProfilesViewModel { get; }

    /// <summary>
    /// Gets the downloads view model.
    /// </summary>
    public DownloadsViewModel DownloadsViewModel { get; }

    /// <summary>
    /// Gets the tools view model.
    /// </summary>
    public ToolsViewModel ToolsViewModel { get; }

    /// <summary>
    /// Gets the settings view model.
    /// </summary>
    public SettingsViewModel SettingsViewModel { get; }

    /// <summary>
    /// Gets the notification manager view model.
    /// </summary>
    public NotificationManagerViewModel NotificationManager { get; }

    /// <summary>
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; } = new();

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public NavigationTab[] AvailableTabs { get; } =
    {
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.Tools,
        NavigationTab.Settings,
    };

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsViewModel,
        NavigationTab.Tools => ToolsViewModel,
        NavigationTab.Settings => SettingsViewModel,
        _ => GameProfilesViewModel,
    };

    /// <summary>
    /// Gets the display name for a navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab.</param>
    /// <returns>The display name.</returns>
    public static string GetTabDisplayName(NavigationTab tab) => tab switch
    {
        NavigationTab.GameProfiles => "Game Profiles",
        NavigationTab.Downloads => "Downloads",
        NavigationTab.Tools => "Tools",
        NavigationTab.Settings => "Settings",
        _ => tab.ToString(),
    };

    /// <summary>
    /// Selects the specified navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab to select.</param>
    [RelayCommand]
    public void SelectTab(NavigationTab tab)
    {
        SelectedTab = tab;
    }

    /// <summary>
    /// Shows the update notification dialog.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ShowUpdateDialogAsync()
    {
        try
        {
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                await GenHub.Features.AppUpdate.Views.UpdateNotificationWindow.ShowAsync(mainWindow);
            }
            else
            {
                _logger?.LogWarning("Cannot show update dialog - main window not found");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show update dialog");
        }
    }

    /// <summary>
    /// Performs asynchronous initialization for the shell and all tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await GameProfilesViewModel.InitializeAsync();
        await DownloadsViewModel.InitializeAsync();
        await ToolsViewModel.InitializeAsync();
        _logger?.LogInformation("MainViewModel initialized");

        // Start background check with cancellation support
        _ = CheckForUpdatesInBackgroundAsync(_initializationCts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Scans for game installations and automatically creates profiles.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ScanAndCreateProfilesAsync()
    {
        _logger?.LogInformation("Starting automatic profile creation from game installations");

        try
        {
            // First scan for installations
            var scanResult = await _gameInstallationDetectionOrchestrator.DetectAllInstallationsAsync();

            if (!scanResult.Success)
            {
                _logger?.LogWarning("Game installation scan failed: {Errors}", string.Join(", ", scanResult.Errors));
                return;
            }

            if (scanResult.Items.Count == 0)
            {
                _logger?.LogInformation("No game installations found");
                return;
            }

            _logger?.LogInformation("Found {Count} game installations, creating profiles", scanResult.Items.Count);

            int createdCount = 0;
            int failedCount = 0;

            foreach (var installation in scanResult.Items)
            {
                if (installation == null) continue;

                try
                {
                    // Skip installations that don't have available game clients
                    if (!installation.AvailableGameClients.Any())
                    {
                        _logger?.LogWarning("Skipping installation {InstallationId} - no available GameClients found", installation.Id);
                        continue;
                    }

                    // Use the first available game client for profile creation
                    var gameClient = installation.AvailableGameClients.First();
                    if (!gameClient.IsValid)
                    {
                        _logger?.LogWarning("Skipping installation {InstallationId} - GameClient {ClientId} is not valid", installation.Id, gameClient.Id);
                        continue;
                    }

                    var gameClientId = gameClient.Id;

                    // Create a profile request from the installation
                    var createRequest = new CreateProfileRequest
                    {
                        Name = $"{installation.InstallationType} Profile",
                        GameInstallationId = installation.Id,
                        GameClientId = gameClientId,
                        Description = $"Auto-created profile for {installation.InstallationType} installation at {installation.InstallationPath}",
                        PreferredStrategy = WorkspaceStrategy.HybridCopySymlink,
                    };

                    var profileResult = await _profileEditorFacade.CreateProfileWithWorkspaceAsync(createRequest);

                    if (profileResult.Success)
                    {
                        createdCount++;
                        _logger?.LogInformation(
                            "Created profile '{ProfileName}' for installation {InstallationId}",
                            profileResult.Data?.Name,
                            installation.Id);
                    }
                    else
                    {
                        failedCount++;
                        _logger?.LogWarning(
                            "Failed to create profile for installation {InstallationId}: {Errors}",
                            installation.Id,
                            string.Join(", ", profileResult.Errors));
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger?.LogError(ex, "Error creating profile for installation {InstallationId}", installation.Id);
                }
            }

            _logger?.LogInformation(
                "Profile creation complete: {Created} created, {Failed} failed",
                createdCount,
                failedCount);

            // Refresh the game profiles view model to show new profiles
            await GameProfilesViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error occurred during automatic profile creation");
        }
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        _initializationCts?.Cancel();
        _initializationCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
            ? dt.MainWindow
            : null;
    }

    /// <summary>
    /// Checks for available updates using Velopack.
    /// </summary>
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Starting background update check");

        try
        {
            // Check if subscribed to a PR - if so, check for PR artifact updates instead
            var settings = _userSettingsService.Get();
            if (settings.SubscribedPrNumber.HasValue)
            {
                _logger?.LogDebug("User subscribed to PR #{PrNumber}, checking for PR artifact updates", settings.SubscribedPrNumber);
                _velopackUpdateManager.SubscribedPrNumber = settings.SubscribedPrNumber;

                // Fetch PR list to populate artifact info
                var prs = await _velopackUpdateManager.GetOpenPullRequestsAsync(cancellationToken);
                var subscribedPr = prs.FirstOrDefault(p => p.Number == settings.SubscribedPrNumber);

                if (subscribedPr?.LatestArtifact != null)
                {
                    // Compare versions (strip build metadata)
                    var currentVersionBase = AppConstants.AppVersion.Split('+')[0];
                    var prVersionBase = subscribedPr.LatestArtifact.Version.Split('+')[0];

                    if (!string.Equals(prVersionBase, currentVersionBase, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if this PR version was dismissed
                        var dismissedVersionBase = settings.DismissedUpdateVersion?.Split('+')[0];

                        if (string.IsNullOrEmpty(dismissedVersionBase) ||
                            !string.Equals(prVersionBase, dismissedVersionBase, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.LogInformation("PR #{PrNumber} artifact update available: {Version}", subscribedPr.Number, prVersionBase);
                            HasUpdateAvailable = true;
                            return;
                        }
                        else
                        {
                            _logger?.LogDebug("PR #{PrNumber} artifact update {Version} was dismissed", subscribedPr.Number, prVersionBase);
                            HasUpdateAvailable = false;
                            return;
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("Already on latest PR #{PrNumber} artifact version", subscribedPr.Number);
                        HasUpdateAvailable = false;
                        return;
                    }
                }
                else
                {
                    _logger?.LogDebug("PR #{PrNumber} has no artifacts or PR not found", settings.SubscribedPrNumber);

                    // Fall through to check main branch updates
                }
            }

            // Check main branch updates (if not subscribed to PR or PR has no artifacts)
            var updateInfo = await _velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);

            // Check both UpdateInfo (from installed app) and GitHub API flag (works in debug too)
            var hasUpdate = updateInfo != null || _velopackUpdateManager.HasUpdateAvailableFromGitHub;

            if (hasUpdate)
            {
                string? latestVersion = null;

                if (updateInfo != null)
                {
                    latestVersion = updateInfo.TargetFullRelease.Version.ToString();
                    _logger?.LogInformation("Update available: {Current} → {Latest}", AppConstants.AppVersion, latestVersion);
                }
                else if (_velopackUpdateManager.LatestVersionFromGitHub != null)
                {
                    latestVersion = _velopackUpdateManager.LatestVersionFromGitHub;
                    _logger?.LogInformation("Update available from GitHub API: {Version}", latestVersion);
                }

                // Strip build metadata for comparison (everything after '+')
                var latestVersionBase = latestVersion?.Split('+')[0];
                var currentVersionBase = AppConstants.AppVersion.Split('+')[0];

                // Check if this version was dismissed by the user
                var settings2 = _userSettingsService.Get();
                var dismissedVersionBase = settings2.DismissedUpdateVersion?.Split('+')[0];

                if (!string.IsNullOrEmpty(latestVersionBase) &&
                    string.Equals(latestVersionBase, dismissedVersionBase, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogDebug("Update {Version} was dismissed by user, hiding notification", latestVersionBase);
                    HasUpdateAvailable = false;
                }

                // Also check if we're already on this version (ignoring build metadata)
                else if (!string.IsNullOrEmpty(latestVersionBase) &&
                         string.Equals(latestVersionBase, currentVersionBase, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogDebug("Already on version {Version} (ignoring build metadata), hiding notification", latestVersionBase);
                    HasUpdateAvailable = false;
                }
                else
                {
                    HasUpdateAvailable = true;
                }
            }
            else
            {
                _logger?.LogDebug("No updates available");
                HasUpdateAvailable = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception in CheckForUpdatesAsync");
            HasUpdateAvailable = false;
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            await CheckForUpdatesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unhandled exception in background update check");
        }
    }

    private void SaveSelectedTab(NavigationTab selectedTab)
    {
        try
        {
            _userSettingsService.Update(settings =>
            {
                settings.LastSelectedTab = selectedTab;
            });

            _ = _userSettingsService.SaveAsync();
            _logger?.LogDebug($"Updated last selected tab to: {selectedTab}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update selected tab setting");
        }
    }

    partial void OnSelectedTabChanged(NavigationTab value)
    {
        OnPropertyChanged(nameof(CurrentTabViewModel));

        // Notify SettingsViewModel when it becomes visible/invisible
        SettingsViewModel.IsViewVisible = value == NavigationTab.Settings;

        // Refresh Downloads tab when it becomes visible
        if (value == NavigationTab.Downloads)
        {
            _ = DownloadsViewModel.OnTabActivatedAsync();
        }

        SaveSelectedTab(value);
    }
}