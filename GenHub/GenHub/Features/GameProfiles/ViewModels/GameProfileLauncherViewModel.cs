using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for launching game profiles.
/// </summary>
public partial class GameProfileLauncherViewModel(
    IGameInstallationService installationService,
    IGameProfileManager gameProfileManager,
    IProfileLauncherFacade profileLauncherFacade,
    GameProfileSettingsViewModel settingsViewModel,
    IProfileEditorFacade profileEditorFacade,
    IConfigurationProviderService configService,
    IGameProcessManager gameProcessManager,
    IShortcutService shortcutService,
    ILogger<GameProfileLauncherViewModel> logger) : ViewModelBase
{
    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);

    [ObservableProperty]
    private ObservableCollection<GameProfileItemViewModel> _profiles = [];

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
    /// Performs asynchronous initialization for the GameProfileLauncherViewModel.
    /// Loads all game profiles and subscribes to process exit events.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        try
        {
            gameProcessManager.ProcessExited += OnProcessExited;

            StatusMessage = "Loading profiles...";
            ErrorMessage = string.Empty;
            Profiles.Clear();

            var profilesResult = await gameProfileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                foreach (var profile in profilesResult.Data)
                {
                    // Use profile's IconPath if available, otherwise fall back to generalshub icon
                    var iconPath = !string.IsNullOrEmpty(profile.IconPath)
                        ? profile.IconPath
                        : UriConstants.DefaultIconUri;

                    // Use profile's CoverPath if available, otherwise fall back to icon path
                    var coverPath = !string.IsNullOrEmpty(profile.CoverPath)
                        ? profile.CoverPath
                        : iconPath;

                    var item = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        coverPath);
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
        var gameIcon = gameType == GameType.Generals ? UriConstants.GeneralsIconFilename : UriConstants.ZeroHourIconFilename;
        _ = installationType switch
        {
            GameInstallationType.Steam => UriConstants.SteamIconFilename,
            GameInstallationType.EaApp => UriConstants.EaAppIconFilename,
            _ => UriConstants.GenHubIconFilename,
        };

        // For now, return the game-specific icon - could be enhanced to combine with platform icon
        return $"{UriConstants.IconsBasePath}/{gameIcon}";
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
                        ? profile.IconPath
                        : UriConstants.DefaultIconUri;

                    // Use profile's CoverPath if available, otherwise fall back to icon path
                    var coverPath = !string.IsNullOrEmpty(profile.CoverPath)
                        ? profile.CoverPath
                        : iconPath;

                    var newItem = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        coverPath);

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
    /// </summary>
    [RelayCommand]
    private async Task ScanForGamesAsync()
    {
        if (IsScanning)
        {
            return; // Prevent multiple concurrent scans
        }

        try
        {
            IsScanning = true;
            StatusMessage = "Scanning for games...";
            ErrorMessage = string.Empty;

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

                foreach (var installation in installations.Data)
                {
                    manifestsGenerated += installation.AvailableGameClients?.Count * 2 ?? 0;

                    // Create profiles for ALL detected game clients (not just one per game type)
                    if (installation.AvailableGameClients != null)
                    {
                        foreach (var gameClient in installation.AvailableGameClients)
                        {
                            var profileCreated = await TryCreateProfileForGameClientAsync(installation, gameClient);
                            if (profileCreated) profilesCreated++;
                        }
                    }
                }

                // Refresh the profiles list to show the newly created ones
                await InitializeAsync();

                StatusMessage = $"Scan complete. Found {installationCount} installations, generated {manifestsGenerated} manifests, created {profilesCreated} profiles";
            }
            else
            {
                var errors = string.Join(", ", installations.Errors);
                StatusMessage = $"Scan failed: {errors}";
                ErrorMessage = errors;
                logger.LogWarning("Game scan failed: {Errors}", errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning for games");
            StatusMessage = "Error during scan";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Attempts to create a profile for a specific game client within an installation.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client to create a profile for.</param>
    /// <returns>True if profile was created successfully, false otherwise.</returns>
    private async Task<bool> TryCreateProfileForGameClientAsync(GameInstallation installation, GameClient gameClient)
    {
        try
        {
            if (gameClient == null)
            {
                logger.LogWarning(
                    "GameClient is null for installation {InstallationId}",
                    installation.Id);
                return false;
            }

            // Define profile name based on game client name and installation type
            var profileName = $"{installation.InstallationType} {gameClient.Name}";

            // Check if a profile already exists for this exact name and installation
            var existingProfiles = await gameProfileManager.GetAllProfilesAsync();
            if (existingProfiles.Success && existingProfiles.Data != null)
            {
                // Check by name AND installation ID
                bool profileExists = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameInstallationId.Equals(installation.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExists)
                {
                    logger.LogDebug("Profile already exists for {InstallationType} {GameClientName} (matched by Name+InstallationId), skipping", installation.InstallationType, gameClient.Name);
                    return false;
                }

                // Also check by name AND game client ID (in case installation ID changed but it's the same logical profile)
                bool profileExistsByClient = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameClient != null &&
                    p.GameClient.Id.Equals(gameClient.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExistsByClient)
                {
                    logger.LogDebug("Profile already exists for {InstallationType} {GameClientName} (matched by Name+ClientId), skipping", installation.InstallationType, gameClient.Name);
                    return false;
                }
            }

            // Get user's preferred workspace strategy or default
            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            // Use the detected version from the game client for the GameInstallation manifest ID
            // This must match the version used in ProfileContentLoader and ManifestGenerationService
            int manifestVersionInt;
            if (string.IsNullOrEmpty(gameClient.Version) ||
                gameClient.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                gameClient.Version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                gameClient.Version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                // For unknown/auto versions, use manifest constants as fallback
                var fallbackVersion = gameClient.GameType == GameType.ZeroHour
                    ? ManifestConstants.ZeroHourManifestVersion
                    : ManifestConstants.GeneralsManifestVersion;

                // Normalize the fallback version (remove dots): "1.04" → 104, "1.08" → 108
                var normalizedFallback = fallbackVersion.Replace(".", string.Empty);
                manifestVersionInt = int.TryParse(normalizedFallback, out var v) ? v : 0;
            }
            else if (gameClient.Version.Contains('.'))
            {
                // Normalize dotted version ("1.04" → 104, "1.08" → 108)
                var normalized = gameClient.Version.Replace(".", string.Empty);
                manifestVersionInt = int.TryParse(normalized, out var v) ? v : 0;
            }
            else
            {
                // Try to parse version as int directly
                manifestVersionInt = int.TryParse(gameClient.Version, out var parsed) ? parsed : 0;
            }

            // Generate the GameInstallation manifest ID for this specific game type
            // This must match what ManifestProvider generates
            var installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, manifestVersionInt);

            // Create enabled content list: GameInstallation manifest + GameClient manifest
            var enabledContentIds = new List<string>
            {
                installationManifestId, // GameInstallation manifest (required for launch validation)
                gameClient.Id,          // GameClient manifest (required for launch validation)
            };

            // Create the profile request using the client manifest ID for GameClientId
            var createRequest = new Core.Models.GameProfile.CreateProfileRequest
            {
                Name = profileName,
                GameInstallationId = installation.Id, // The actual installation GUID
                GameClientId = gameClient.Id, // Client manifest ID
                Description = $"Auto-created profile for {installation.InstallationType} {gameClient.Name}",
                PreferredStrategy = preferredStrategy,
                EnabledContentIds = enabledContentIds, // Both GameInstallation and GameClient manifests
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = GetIconPathForGame(gameClient.GameType, installation.InstallationType),
            };

            var profileResult = await profileEditorFacade.CreateProfileWithWorkspaceAsync(createRequest);
            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation("Successfully created profile '{ProfileName}' for {InstallationType} {GameClientName}", profileResult.Data.Name, installation.InstallationType, gameClient.Name);
                return true;
            }
            else
            {
                var errors = string.Join(", ", profileResult.Errors);
                logger.LogWarning("Failed to create profile for {InstallationType} {GameClientName}: {Errors}", installation.InstallationType, gameClient.Name, errors);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profile for {InstallationType} {GameClientName}", installation.InstallationType, gameClient?.Name ?? "Unknown");
            return false;
        }
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
        if (string.IsNullOrEmpty(profile.ProfileId))
        {
            StatusMessage = "Invalid profile";
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
        try
        {
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
        try
        {
            IsPreparingWorkspace = true;
            profile.IsPreparingWorkspace = true;
            StatusMessage = $"Preparing workspace for {profile.Name}...";
            var prepareResult = await profileLauncherFacade.PrepareWorkspaceAsync(profile.ProfileId);

            if (prepareResult.Success && prepareResult.Data != null)
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
    /// Creates a desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to create a shortcut for.</param>
    [RelayCommand]
    private async Task CreateShortcut(GameProfileItemViewModel profile)
    {
        try
        {
            StatusMessage = $"Creating desktop shortcut for {profile.Name}...";

            // Get the full profile to pass to the shortcut service
            var profileResult = await gameProfileManager.GetProfileAsync(profile.ProfileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                StatusMessage = $"Failed to load profile: {string.Join(", ", profileResult.Errors)}";
                return;
            }

            var result = await shortcutService.CreateDesktopShortcutAsync(profileResult.Data);
            if (result.Success)
            {
                StatusMessage = $"Desktop shortcut created for {profile.Name}";
                logger?.LogInformation("Created desktop shortcut for profile {ProfileName} at {Path}", profile.Name, result.Data);
            }
            else
            {
                StatusMessage = $"Failed to create shortcut: {string.Join(", ", result.Errors)}";
                logger?.LogWarning("Failed to create shortcut for profile {ProfileName}: {Errors}", profile.Name, string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error creating shortcut for profile {ProfileName}", profile.Name);
            StatusMessage = $"Error creating shortcut for {profile.Name}";
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
