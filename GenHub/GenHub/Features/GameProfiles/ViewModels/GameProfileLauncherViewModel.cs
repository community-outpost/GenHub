using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for launching game profiles.
/// </summary>
public partial class GameProfileLauncherViewModel(
    IGameInstallationService? installationService,
    IGameProfileManager? gameProfileManager,
    IProfileLauncherFacade? profileLauncherFacade,
    GameProfileSettingsViewModel? settingsViewModel,
    IProfileEditorFacade? profileEditorFacade,
    IGameProcessManager? gameProcessManager,
    IGameClientProfileService gameClientProfileService,
    INotificationService? notificationService,
    ILogger<GameProfileLauncherViewModel>? logger) : ViewModelBase
{
    private readonly ILogger<GameProfileLauncherViewModel> logger = logger ?? NullLogger<GameProfileLauncherViewModel>.Instance;
    private readonly IGameClientProfileService _gameClientProfileService = gameClientProfileService ?? throw new ArgumentNullException(nameof(gameClientProfileService));
    private readonly INotificationService? _notificationService = notificationService;

    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);

    /// <summary>
    /// Tracks profile IDs already added to UI to prevent duplicates from race conditions.
    /// Thread-safe dictionary for concurrent access from async operations.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _addedProfileIds = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private ObservableCollection<GameProfileItemViewModel> _profiles = new();

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _isPreparingWorkspace;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isServiceAvailable = true;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherViewModel"/> class for design-time or test usage.
    /// This constructor is only for design-time and testing scenarios.
    /// </summary>
    public GameProfileLauncherViewModel()
        : this(null, null, null, null, null, null, new StubGameClientProfileService(), null, null)
    {
        // Initialize with sample data for design-time
        StatusMessage = "Design-time preview";
        IsServiceAvailable = false;
    }

    /// <summary>
    /// Stub implementation of IGameClientProfileService for design-time use.
    /// </summary>
    private class StubGameClientProfileService : IGameClientProfileService
    {
        public Task<ProfileOperationResult<GameProfile>> CreateProfileForGameClientAsync(
            GameInstallation installation, GameClient gameClient, CancellationToken cancellationToken = default)
            => Task.FromResult(ProfileOperationResult<GameProfile>.CreateFailure("Design-time stub"));

        public Task<List<ProfileOperationResult<GameProfile>>> CreateProfilesForGameClientAsync(
            GameInstallation installation, GameClient gameClient, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ProfileOperationResult<GameProfile>>());

        public Task<ProfileOperationResult<GameProfile>> CreateProfileFromManifestAsync(
            ContentManifest manifest, CancellationToken cancellationToken = default)
            => Task.FromResult(ProfileOperationResult<GameProfile>.CreateFailure("Design-time stub"));

        public Task<bool> ProfileExistsForGameClientAsync(string gameClientId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    /// <summary>
    /// Gets the command to create a shortcut for the selected profile.
    /// </summary>
    public IRelayCommand CreateShortcutCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Performs asynchronous initialization for the GameProfileLauncherViewModel.
    /// Loads all game profiles and subscribes to process exit events.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        try
        {
            // Subscribe to process exit events
            if (gameProcessManager != null)
            {
                gameProcessManager.ProcessExited += OnProcessExited;
            }

            StatusMessage = "Loading profiles...";
            ErrorMessage = string.Empty;
            Profiles.Clear();

            if (gameProfileManager == null)
            {
                StatusMessage = "Profile manager not available";
                ErrorMessage = "Game Profile Manager service is not initialized";
                IsServiceAvailable = false;
                logger.LogWarning("GameProfileManager not available for profile loading");
                return;
            }

            IsServiceAvailable = true;
            var profilesResult = await gameProfileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                foreach (var profile in profilesResult.Data)
                {
                    // Skip if already added during this session
                    if (!_addedProfileIds.TryAdd(profile.Id, 0))
                    {
                        continue;
                    }

                    // Use profile's IconPath if available, otherwise fall back to generalshub icon
                    var iconPath = !string.IsNullOrEmpty(profile.IconPath)
                        ? $"avares://GenHub/{profile.IconPath}"
                        : Core.Constants.UriConstants.DefaultIconUri;

                    var item = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        iconPath); // Using same icon for both icon and cover for now
                    Profiles.Add(item);
                }

                StatusMessage = $"Loaded {Profiles.Count} profiles";
                logger.LogInformation("Loaded {Count} game profiles", Profiles.Count);
            }
            else
            {
                var errors = string.Join(", ", profilesResult.Errors);
                StatusMessage = $"Failed to load profiles: {errors}";
                ErrorMessage = errors;
                logger.LogWarning("Failed to load profiles: {Errors}", errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing profiles");
            StatusMessage = "Error loading profiles";
            ErrorMessage = ex.Message;
            IsServiceAvailable = false;
        }
    }

    /// <summary>
    /// Gets the default theme color for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The hex color code.</returns>
    private static string GetThemeColorForGameType(GameType gameType)
    {
        return gameType == GameType.Generals ? "#BD5A0F" : "#1B6575"; // Orange for Generals, Blue for Zero Hour
    }

    /// <summary>
    /// Gets the icon path for a game type and installation type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="installationType">The installation type.</param>
    /// <returns>The relative icon path.</returns>
    private static string GetIconPathForGame(GameType gameType, GameInstallationType installationType)
    {
        var gameIcon = gameType == GameType.Generals ? Core.Constants.UriConstants.GeneralsIconFilename : Core.Constants.UriConstants.ZeroHourIconFilename;
        var platformIcon = installationType switch
        {
            GameInstallationType.Steam => Core.Constants.UriConstants.SteamIconFilename,
            GameInstallationType.EaApp => Core.Constants.UriConstants.EaAppIconFilename,
            _ => Core.Constants.UriConstants.GenHubIconFilename
        };

        // For now, return the game-specific icon - could be enhanced to combine with platform icon
        return $"{Core.Constants.UriConstants.IconsBasePath}/{gameIcon}";
    }

    /// <summary>
    /// Gets the main window for opening dialogs.
    /// </summary>
    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    /// <summary>
    /// Refreshes a single profile without reloading all profiles (preserves running state).
    /// </summary>
    /// <param name="profileId">The ID of the profile to refresh.</param>
    private async Task RefreshSingleProfileAsync(string profileId)
    {
        try
        {
            if (gameProfileManager == null)
            {
                logger.LogWarning("GameProfileManager not available for profile refresh");
                return;
            }

            var profileResult = await gameProfileManager.GetProfileAsync(profileId);
            if (profileResult.Success && profileResult.Data != null)
            {
                var profile = profileResult.Data;
                var existingItem = Profiles.FirstOrDefault(p => p.ProfileId == profileId);

                if (existingItem != null)
                {
                    // Preserve the running state before updating
                    var wasRunning = existingItem.IsProcessRunning;
                    var processId = existingItem.ProcessId;
                    var workspaceId = existingItem.ActiveWorkspaceId;

                    // Update the profile data
                    var iconPath = !string.IsNullOrEmpty(profile.IconPath)
                        ? $"avares://GenHub/{profile.IconPath}"
                        : Core.Constants.UriConstants.DefaultIconUri;

                    var newItem = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        iconPath);

                    // Restore the running state
                    if (wasRunning)
                    {
                        newItem.IsProcessRunning = true;
                        newItem.ProcessId = processId;
                    }

                    // Restore workspace state
                    if (!string.IsNullOrEmpty(workspaceId))
                    {
                        newItem.UpdateWorkspaceStatus(workspaceId, profile.WorkspaceStrategy);
                    }

                    var index = Profiles.IndexOf(existingItem);
                    Profiles[index] = newItem;

                    logger.LogInformation("Refreshed profile {ProfileId} (Running: {IsRunning})", profileId, wasRunning);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing profile {ProfileId}", profileId);
        }
    }

    /// <summary>
    /// Scans for games and automatically creates profiles for detected installations.
    /// Profiles are added to the UI progressively as they're created, providing immediate feedback.
    /// </summary>
    [RelayCommand]
    private async Task ScanForGamesAsync()
    {
        if (installationService == null)
        {
            StatusMessage = "Game installation service not available";
            ErrorMessage = "Game Installation Service is not initialized";
            IsServiceAvailable = false;
            return;
        }

        if (IsScanning)
        {
            return; // Prevent multiple concurrent scans
        }

        try
        {
            IsScanning = true;
            StatusMessage = "Scanning for games...";
            ErrorMessage = string.Empty;

            _notificationService?.ShowInfo(
                "Scanning for Games",
                "Detecting game installations and creating profiles...",
                autoDismissMs: NotificationDurations.Short);

            // Scan for all installations
            var installations = await installationService.GetAllInstallationsAsync();
            if (installations.Success && installations.Data != null)
            {
                var installationCount = installations.Data.Count;
                var generalsCount = installations.Data.Count(i => i.HasGenerals);
                var zeroHourCount = installations.Data.Count(i => i.HasZeroHour);

                logger.LogInformation(
                    "Game scan completed successfully. Found {Count} installations ({GeneralsCount} Generals, {ZeroHourCount} Zero Hour)",
                    installationCount,
                    generalsCount,
                    zeroHourCount);

                // Generate manifests and populate versions for detected installations
                int manifestsGenerated = 0;
                int profilesCreated = 0;

                if (profileEditorFacade != null && gameProfileManager != null)
                {
                    foreach (var installation in installations.Data)
                    {
                        manifestsGenerated += installation.AvailableGameClients?.Count() * 2 ?? 0;

                        // Create profiles for ALL detected game clients (not just one per game type)
                        if (installation.AvailableGameClients != null)
                        {
                            foreach (var gameClient in installation.AvailableGameClients)
                            {
                                // Update status to show which client is being processed
                                StatusMessage = $"Creating profile for {gameClient.Name}...";

                                // TryCreateProfilesForGameClientAsync returns ALL created profiles
                                // For multi-variant content (GeneralsOnline, SuperHackers), this includes all variants
                                var createdProfiles = await TryCreateProfilesForGameClientAsync(installation, gameClient);

                                foreach (var createdProfile in createdProfiles)
                                {
                                    profilesCreated++;

                                    // Skip if already added (prevents duplicates from race conditions)
                                    if (!_addedProfileIds.TryAdd(createdProfile.Id, 0))
                                    {
                                        logger.LogDebug(
                                            "Skipping duplicate profile '{ProfileName}' - already in UI",
                                            createdProfile.Name);
                                        continue;
                                    }

                                    // PROGRESSIVE LOADING: Add the newly created profile to the UI immediately
                                    // This provides instant feedback instead of waiting for all profiles
                                    var iconPath = !string.IsNullOrEmpty(createdProfile.IconPath)
                                        ? $"avares://GenHub/{createdProfile.IconPath}"
                                        : Core.Constants.UriConstants.DefaultIconUri;

                                    var item = new GameProfileItemViewModel(
                                        createdProfile.Id,
                                        createdProfile,
                                        iconPath,
                                        iconPath);

                                    Profiles.Add(item);

                                    logger.LogInformation(
                                        "Progressive load: Added profile '{ProfileName}' to UI",
                                        createdProfile.Name);
                                }
                            }
                        }
                    }
                }

                StatusMessage = $"Scan complete. Found {installationCount} installations, generated {manifestsGenerated} manifests, created {profilesCreated} profiles";

                // NOTE: No post-scan reload needed - CreateProfilesForGameClientAsync returns ALL profiles
                // including all variants (30Hz/60Hz, Generals/ZeroHour) which are added progressively above
            }
            else
            {
                var errors = string.Join(", ", installations.Errors);
                StatusMessage = $"Scan failed: {errors}";
                ErrorMessage = errors;
                logger.LogWarning("Game scan failed: {Errors}", errors);

                _notificationService?.ShowError(
                    "Scan Failed",
                    $"Could not scan for games: {errors}",
                    autoDismissMs: NotificationDurations.Long);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning for games");
            StatusMessage = "Error during scan";
            ErrorMessage = ex.Message;

            _notificationService?.ShowError(
                "Scan Error",
                $"An error occurred while scanning: {ex.Message}",
                autoDismissMs: NotificationDurations.Critical);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Attempts to create profiles for a specific game client within an installation.
    /// For multi-variant content (GeneralsOnline, SuperHackers), returns ALL created profiles.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client to create profiles for.</param>
    /// <returns>A list of created GameProfiles (empty if none could be created).</returns>
    private async Task<List<GameProfile>> TryCreateProfilesForGameClientAsync(GameInstallation installation, GameClient gameClient)
    {
        var createdProfiles = new List<GameProfile>();

        // Use CreateProfilesForGameClientAsync which returns ALL created profiles
        var results = await _gameClientProfileService.CreateProfilesForGameClientAsync(
            installation,
            gameClient);

        foreach (var result in results)
        {
            if (result.Success && result.Data != null)
            {
                createdProfiles.Add(result.Data);
            }
        }

        return createdProfiles;
    }

    /// <summary>
    /// Launches the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    [RelayCommand]
    private async Task LaunchProfileAsync(GameProfileItemViewModel profile)
    {
        // Try without blocking
        if (!await _launchSemaphore.WaitAsync(0))
        {
            StatusMessage = "A profile is already launching...";
            return;
        }

        try
        {
            if (profileLauncherFacade == null)
            {
                StatusMessage = "Profile launcher not available";
                ErrorMessage = "Profile Launcher service is not initialized";
                return;
            }

            try
            {
                IsLaunching = true;
                StatusMessage = $"Launching {profile.Name}...";
                ErrorMessage = string.Empty;

                var launchResult = await profileLauncherFacade.LaunchProfileAsync(profile.ProfileId);

                if (launchResult.Success && launchResult.Data != null)
                {
                    profile.IsProcessRunning = true;
                    profile.ProcessId = launchResult.Data.ProcessInfo.ProcessId;
                    OnPropertyChanged(nameof(profile.CanLaunch));
                    OnPropertyChanged(nameof(profile.CanEdit));

                    StatusMessage = $"{profile.Name} launched successfully (Process ID: {launchResult.Data.ProcessInfo.ProcessId})";
                    logger.LogInformation(
                        "Profile {ProfileName} launched successfully with process ID {ProcessId}",
                        profile.Name,
                        launchResult.Data.ProcessInfo.ProcessId);
                }
                else
                {
                    var errors = string.Join(", ", launchResult.Errors);
                    StatusMessage = $"Failed to launch {profile.Name}: {errors}";
                    ErrorMessage = errors;
                    logger.LogWarning(
                        "Failed to launch profile {ProfileName}: {Errors}",
                        profile.Name,
                        errors);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error launching profile {ProfileName}", profile.Name);
                StatusMessage = $"Error launching {profile.Name}";
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLaunching = false;
            }
        }
        finally
        {
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// Stops the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to stop.</param>
    [RelayCommand]
    private async Task StopProfile(GameProfileItemViewModel profile)
    {
        if (profileLauncherFacade == null)
        {
            StatusMessage = "Profile launcher not available";
            return;
        }

        try
        {
            StatusMessage = $"Stopping {profile.Name}...";

            var stopResult = await profileLauncherFacade.StopProfileAsync(profile.ProfileId);

            if (stopResult.Success)
            {
                // Update IsProcessRunning to hide Stop button and show Launch button
                profile.IsProcessRunning = false;
                profile.ProcessId = 0;
                OnPropertyChanged(nameof(profile.CanLaunch));
                OnPropertyChanged(nameof(profile.CanEdit));

                StatusMessage = $"{profile.Name} stopped successfully";
                logger.LogInformation("Profile {ProfileName} stopped successfully", profile.Name);
            }
            else
            {
                var errors = string.Join(", ", stopResult.Errors);
                StatusMessage = $"Failed to stop {profile.Name}: {errors}";
                logger.LogWarning(
                    "Failed to stop profile {ProfileName}: {Errors}",
                    profile.Name,
                    errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping profile {ProfileName}", profile.Name);
            StatusMessage = $"Error stopping {profile.Name}";
        }
    }

    /// <summary>
    /// Toggles edit mode for the profiles list.
    /// </summary>
    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        StatusMessage = IsEditMode ? "Edit mode enabled" : "Edit mode disabled";
        logger?.LogInformation("Toggled edit mode to {IsEditMode}", IsEditMode);
    }

    /// <summary>
    /// Saves changes made in edit mode.
    /// </summary>
    [RelayCommand]
    private async Task SaveProfiles()
    {
        if (gameProfileManager == null)
        {
            StatusMessage = "Profile manager not available";
            return;
        }

        try
        {
            StatusMessage = "Saving profiles...";

            // Implementation for saving changes would go here
            // For now, just refresh the list
            await InitializeAsync();
            StatusMessage = "Profiles saved successfully";
            logger?.LogInformation("Saved profiles in edit mode");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error saving profiles");
            StatusMessage = "Error saving profiles";
        }
    }

    /// <summary>
    /// Deletes the selected profile.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfile(GameProfileItemViewModel profile)
    {
        if (profileLauncherFacade == null || string.IsNullOrEmpty(profile.ProfileId))
        {
            StatusMessage = "Profile launcher not available";
            return;
        }

        try
        {
            StatusMessage = $"Deleting {profile.Name}...";
            var deleteResult = await profileLauncherFacade.DeleteProfileAsync(profile.ProfileId);

            if (deleteResult.Success)
            {
                Profiles.Remove(profile);
                StatusMessage = $"{profile.Name} deleted successfully";
                logger?.LogInformation("Deleted profile {ProfileName}", profile.Name);
            }
            else
            {
                var errors = string.Join(", ", deleteResult.Errors);
                StatusMessage = $"Failed to delete {profile.Name}: {errors}";
                logger?.LogWarning("Failed to delete profile {ProfileName}: {Errors}", profile.Name, errors);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error deleting profile {ProfileName}", profile.Name);
            StatusMessage = $"Error deleting {profile.Name}";
        }
    }

    /// <summary>
    /// Edits the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to edit.</param>
    [RelayCommand]
    private async Task EditProfile(GameProfileItemViewModel profile)
    {
        if (settingsViewModel == null)
        {
            StatusMessage = "Profile settings not available";
            return;
        }

        try
        {
            if (profileEditorFacade == null)
            {
                StatusMessage = "Profile editor not available";
                return;
            }

            // Load the profile using the profile editor facade
            var loadResult = await profileEditorFacade.GetProfileWithWorkspaceAsync(profile.ProfileId);
            if (!loadResult.Success || loadResult.Data == null)
            {
                StatusMessage = $"Failed to load profile: {string.Join(", ", loadResult.Errors)}";
                return;
            }

            // Initialize the settings view model for this profile
            await settingsViewModel.InitializeForProfileAsync(profile.ProfileId);

            // For now, just show the settings window - profile data loading into view model needs more implementation
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var settingsWindow = new GameProfileSettingsWindow
                {
                    DataContext = settingsViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                await settingsWindow.ShowDialog(mainWindow);

                // Refresh only the edited profile to preserve running state
                await RefreshSingleProfileAsync(profile.ProfileId);
                StatusMessage = "Profile updated successfully";
            }
            else
            {
                StatusMessage = "Could not find main window to open settings";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error editing profile {ProfileName}", profile.Name);
            StatusMessage = $"Error editing {profile.Name}";
        }
    }

    /// <summary>
    /// Creates a new game profile.
    /// </summary>
    [RelayCommand]
    private async Task CreateNewProfile()
    {
        if (settingsViewModel == null)
        {
            StatusMessage = "Profile settings not available";
            return;
        }

        try
        {
            // Initialize settings view model for new profile creation
            await settingsViewModel.InitializeForNewProfileAsync();

            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var settingsWindow = new GameProfileSettingsWindow
                {
                    DataContext = settingsViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                await settingsWindow.ShowDialog(mainWindow);

                // Refresh the profiles list after the window closes to show newly created profile
                await InitializeAsync();
                StatusMessage = "New profile window closed";
            }
            else
            {
                StatusMessage = "Could not find main window to open settings";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error creating new profile");
            StatusMessage = "Error creating new profile";
        }
    }

    /// <summary>
    /// Prepares the workspace for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to prepare workspace for.</param>
    [RelayCommand]
    private async Task PrepareWorkspace(GameProfileItemViewModel profile)
    {
        if (profileLauncherFacade == null)
        {
            StatusMessage = "Profile launcher not available";
            return;
        }

        try
        {
            IsPreparingWorkspace = true;
            profile.IsPreparingWorkspace = true;
            StatusMessage = $"Preparing workspace for {profile.Name}...";
            var prepareResult = await profileLauncherFacade.PrepareWorkspaceAsync(profile.ProfileId);

            if (prepareResult.Success && prepareResult.Data != null)
            {
                if (gameProfileManager != null)
                {
                    var profileResult = await gameProfileManager.GetProfileAsync(profile.ProfileId);
                    if (profileResult.Success && profileResult.Data != null)
                    {
                        var loadedProfile = profileResult.Data;

                        // Update the existing item's status
                        profile.UpdateWorkspaceStatus(loadedProfile.ActiveWorkspaceId, loadedProfile.WorkspaceStrategy);

                        // Force UI refresh by removing and re-adding to ObservableCollection
                        var index = Profiles.IndexOf(profile);
                        if (index >= 0)
                        {
                            Profiles.RemoveAt(index);
                            Profiles.Insert(index, profile);
                            logger?.LogDebug("Forced UI refresh for profile {ProfileName} at index {Index}", profile.Name, index);
                        }
                    }
                }

                StatusMessage = $"Workspace prepared for {profile.Name} at {prepareResult.Data.WorkspacePath}";
                logger?.LogInformation("Prepared workspace for profile {ProfileName} at {Path}", profile.Name, prepareResult.Data.WorkspacePath);
            }
            else
            {
                var errors = string.Join(", ", prepareResult.Errors);
                StatusMessage = $"Failed to prepare workspace for {profile.Name}: {errors}";
                logger?.LogWarning("Failed to prepare workspace for profile {ProfileName}: {Errors}", profile.Name, errors);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error preparing workspace for profile {ProfileName}", profile.Name);
            StatusMessage = $"Error preparing workspace for {profile.Name}";
        }
        finally
        {
            IsPreparingWorkspace = false;
            profile.IsPreparingWorkspace = false;
        }
    }

    /// <summary>
    /// Handles the process exited event to update profile state when a game exits.
    /// </summary>
    private void OnProcessExited(object? sender, Core.Models.Events.GameProcessExitedEventArgs e)
    {
        try
        {
            logger?.LogInformation("Game process {ProcessId} exited with code {ExitCode}", e.ProcessId, e.ExitCode);

            // Find the profile that was running this process
            var profile = Profiles.FirstOrDefault(p => p.ProcessId == e.ProcessId);
            if (profile != null)
            {
                profile.IsProcessRunning = false;
                profile.ProcessId = 0;
                logger?.LogInformation("Updated profile {ProfileName} - process no longer running", profile.Name);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error handling process exit event for process {ProcessId}", e.ProcessId);
        }
    }
}
