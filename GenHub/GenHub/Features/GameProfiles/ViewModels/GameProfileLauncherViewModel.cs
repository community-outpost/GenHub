using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;

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
    IPublisherProfileOrchestrator publisherProfileOrchestrator,
    ISteamManifestPatcher steamManifestPatcher,
    ProfileResourceService profileResourceService,
    IGameClientDetector gameClientDetector,
    INotificationService notificationService,
    ISetupWizardService setupWizardService,
    IManifestGenerationService manifestGenerationService,
    IContentManifestPool contentManifestPool,
    IDialogService dialogService,
    ILogger<GameProfileLauncherViewModel> logger) : ViewModelBase,
    IRecipient<ProfileCreatedMessage>,
    IRecipient<ProfileUpdatedMessage>,
    IRecipient<ProfileListUpdatedMessage>
{
    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);
    private readonly System.Timers.Timer _headerCollapseTimer = new(TimeIntervals.HeaderCollapseDelayMs);

    [ObservableProperty]
    private ObservableCollection<GameProfileItemViewModel> _profiles = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditProfileCommand))]
    private GameProfileItemViewModel? _selectedProfile;

    /// <summary>
    /// Gets a value indicating whether a profile can be edited.
    /// </summary>
    public bool CanEditProfile => SelectedProfile != null;

    partial void OnSelectedProfileChanged(GameProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanEditProfile));
        LaunchProfileCommand.NotifyCanExecuteChanged();
        EditProfileCommand.NotifyCanExecuteChanged();
    }

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

    [ObservableProperty]
    private bool _isHeaderExpanded = true;

    /// <summary>
    /// Performs asynchronous initialization for the GameProfileLauncherViewModel.
    /// Loads all game profiles and subscribes to process exit events.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        // Reset header state on initialization/activation
        ResetHeaderState();

        try
        {
            // Set up timer
            _headerCollapseTimer.AutoReset = false;
            _headerCollapseTimer.Elapsed += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                {
                    IsHeaderExpanded = false;
                });
            };

            _headerCollapseTimer.Start();

            gameProcessManager.ProcessExited += OnProcessExited;

            StatusMessage = "Loading profiles...";
            ErrorMessage = string.Empty;
            Profiles.Clear();

            var profilesResult = await gameProfileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                foreach (var profile in profilesResult.Data)
                {
                    // Use ProfileResourceService to get default paths based on game type if profile paths are missing
                    var gameTypeStr = profile.GameClient?.GameType.ToString() ?? "ZeroHour";

                    var iconPath = !string.IsNullOrEmpty(profile.IconPath)
                        ? profile.IconPath
                        : UriConstants.DefaultIconUri;

                    var coverPath = !string.IsNullOrEmpty(profile.CoverPath)
                        ? profile.CoverPath
                        : profileResourceService.GetDefaultCoverPath(gameTypeStr);

                    var item = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        coverPath)
                    {
                        LaunchAction = LaunchProfileAsync,
                        EditProfileAction = EditProfile,
                        DeleteProfileAction = DeleteProfile,
                        CreateShortcutAction = CreateShortcut,
                    };

                    // Add to collection before the "Add New Profile" button (which is always at the end)
                    // If the last item is AddProfileItemViewModel, insert before it
                    if (Profiles.Count > 0 && Profiles[^1] is AddProfileItemViewModel)
                    {
                        Profiles.Insert(Profiles.Count - 1, item);
                    }
                    else
                    {
                        Profiles.Add(item);
                    }
                }

                // Add "Add New Profile" item at the end
                Profiles.Add(new AddProfileItemViewModel());

                var profileCount = Profiles.Count - 1;
                StatusMessage = $"Loaded {profileCount} profiles";
                logger.LogInformation("Loaded {Count} game profiles", profileCount);
            }
            else
            {
                var errors = string.Join(", ", profilesResult.Errors);
                StatusMessage = $"Failed to load profiles: {errors}";
                ErrorMessage = errors;
                logger.LogWarning("Failed to load profiles: {Errors}", errors);
            }

            // Register for profile messages on first initialization only
            if (!WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(this))
            {
                WeakReferenceMessenger.Default.RegisterAll(this);
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
    /// Receives notification when a new profile is created and refreshes the profiles list.
    /// </summary>
    /// <param name="message">The profile created message.</param>
    public void Receive(ProfileCreatedMessage message)
    {
        logger.LogInformation("Profile created notification received for {ProfileName}, adding to UI", message.Profile.Name);

        // Add profile to UI on UI thread
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                AddProfileToUI(message.Profile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding profile to UI after creation");
            }
        });
    }

    /// <summary>
    /// Receives notification when a profile is updated and refreshes the profiles list.
    /// </summary>
    /// <param name="message">The profile updated message.</param>
    public void Receive(ProfileUpdatedMessage message)
    {
        logger.LogInformation("Profile updated notification received for {ProfileName}, refreshing list", message.Profile.Name);

        // Refresh specific profile on UI thread to preserve state of others
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await RefreshSingleProfileAsync(message.Profile.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing profile after update");
            }
        });
    }

    /// <summary>
    /// Receives notification when the profile list has been updated (bulk changes).
    /// </summary>
    /// <param name="message">The profile list updated message.</param>
    public void Receive(ProfileListUpdatedMessage message)
    {
        logger.LogInformation("Profile list updated notification received, refreshing list");

        // Refresh profiles on UI thread
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing profiles after list update");
            }
        });
    }

    /// <summary>
    /// Called when the tab is activated/navigated to.
    /// Resets the header state to expanded.
    /// </summary>
    public void OnTabActivated()
    {
        ResetHeaderState();
    }

    private bool _isHovering;

    /// <summary>
    /// Resets the header state to expanded and restarts the auto-collapse timer.
    /// </summary>
    public void ResetHeaderState()
    {
        IsHeaderExpanded = true;
        _headerCollapseTimer.Stop();

        // Only start the auto-collapse timer if the user is NOT currently hovering
        if (!_isHovering)
        {
            _headerCollapseTimer.Start();
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    /// <summary>
    /// Gets the default theme color for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The hex color code.</returns>
    private static string GetThemeColorForGameType(GameType gameType)
    {
        return gameType == GameType.Generals ? UiConstants.GeneralsThemeColor : UiConstants.ZeroHourThemeColor; // Orange for Generals, Blue for Zero Hour
    }

    /// <summary>
    /// Gets the icon path for a game type and installation type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The relative icon path.</returns>
    private static string GetIconPathForGame(GameType gameType)
    {
        var gameIcon = gameType == GameType.Generals ? UriConstants.GeneralsIconFilename : UriConstants.ZeroHourIconFilename;

        // For now, return the game-specific icon - could be enhanced to combine with platform icon
        return $"{UriConstants.IconsBasePath}/{gameIcon}";
    }

    /// <summary>
    /// Checks if the installation has any publisher-based game clients (GeneralsOnline, TheSuperHackers).
    /// </summary>
    private static bool HasPublisherClients(GameInstallation installation)
    {
        return installation.AvailableGameClients != null &&
               installation.AvailableGameClients.Any(c => c.IsPublisherClient);
    }

    /// <summary>
    /// Checks if a game client is a standard base game client (not a publisher client).
    /// </summary>
    private static bool IsStandardGameClient(GameClient client)
    {
        return !client.IsPublisherClient;
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
                var existingItem = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProfileId == profileId);

                if (existingItem != null)
                {
                    // Use UpdateFromProfile to refresh the existing ViewModel in-place
                    // This preserves running state and just updates displayed properties (especially GameVersion)
                    existingItem.UpdateFromProfile(profile);

                    logger.LogInformation("Refreshed profile {ProfileId} in-place (Running: {IsRunning})", profileId, existingItem.IsProcessRunning);
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
    /// Implements smart profile creation: skips base Generals/ZeroHour if publisher clients exist,
    /// or prompts for Community Patch installation.
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
            IsHeaderExpanded = true;
            _headerCollapseTimer.Stop(); // Ensure header stays open during scan

            StatusMessage = "Scanning for games...";
            ErrorMessage = string.Empty;

            // Scan for all installations
            var installations = await installationService.GetAllInstallationsAsync();
            if (installations.Success && installations.Data != null)
            {
                // Convert to mutable list to allow adding manual installations
                var installationsList = installations.Data.ToList();

                // Check if no installations were found and prompt for manual selection
                if (installationsList.Count == 0)
                {
                    logger.LogInformation("No game installations found, prompting user for manual directory selection");

                    var manualInstallation = await PromptForManualGameDirectoryAsync();
                    if (manualInstallation != null)
                    {
                        // Ensure paths are populated
                        manualInstallation.Fetch();

                        // Detect game clients for the manual installation (also generates GameClient manifests)
                        var detectionResult = await gameClientDetector.DetectGameClientsFromInstallationsAsync([manualInstallation]);
                        if (detectionResult.Success && detectionResult.Items?.Count > 0)
                        {
                            manualInstallation.PopulateGameClients(detectionResult.Items);
                        }

                        // Create and register GameInstallation manifests to the pool
                        await CreateAndRegisterManualInstallationManifestsAsync(manualInstallation);

                        // Register the manual installation with the service so it's available globally (e.g. for profile creation)
                        await installationService.RegisterManualInstallationAsync(manualInstallation);

                        // Add the manually selected installation to the list
                        installationsList.Add(manualInstallation);
                        logger.LogInformation("User provided manual installation, proceeding with profile creation");
                    }
                    else
                    {
                        logger.LogInformation("User cancelled manual directory selection");
                        StatusMessage = "No installations found. Scan cancelled.";
                        return;
                    }
                }

                var installationCount = installationsList.Count;
                var generalsCount = installationsList.Count(i => i.HasGenerals);
                var zeroHourCount = installationsList.Count(i => i.HasZeroHour);

                logger.LogInformation(
                    "Game scan completed. Found {Count} installations ({GeneralsCount} Generals, {ZeroHourCount} Zero Hour)",
                    installationCount,
                    generalsCount,
                    zeroHourCount);

                // Run Setup Wizard via Service
                var wizardResult = await setupWizardService.RunSetupWizardAsync(installationsList);

                var cpDecision = wizardResult.CommunityPatchAction;
                var goDecision = wizardResult.GeneralsOnlineAction;
                var shDecision = wizardResult.SuperHackersAction;

                bool wizardConfirmed = wizardResult.Confirmed;

                // Check if any patches were selected globally
                bool anyPatchSelectedGlobally =
                    (cpDecision != GameClientConstants.WizardActionTypes.Decline && cpDecision != GameClientConstants.WizardActionTypes.None) ||
                    (goDecision != GameClientConstants.WizardActionTypes.Decline && goDecision != GameClientConstants.WizardActionTypes.None) ||
                    (shDecision != GameClientConstants.WizardActionTypes.Decline && shDecision != GameClientConstants.WizardActionTypes.None);

                // 4. Execution Phase: Apply decisions per installation
                int profilesCreated = 0;
                foreach (var installation in installationsList)
                {
                    if (installation.AvailableGameClients == null || installation.AvailableGameClients.Count == 0)
                    {
                        continue;
                    }

                    logger.LogInformation("Processing installation: {InstallationId} ({Type})", installation.Id, installation.InstallationType);
                    bool anyPatchHandled = false;

                    // Execute CP Decision
                    if (cpDecision != GameClientConstants.WizardActionTypes.Decline && cpDecision != GameClientConstants.WizardActionTypes.None)
                    {
                        var cpClient = installation.AvailableGameClients.FirstOrDefault(c => c.PublisherType == CommunityOutpostConstants.PublisherType);
                        if (cpClient != null || cpDecision == GameClientConstants.WizardActionTypes.Install)
                        {
                            var clientToUse = cpClient ?? new GameClient { Id = GameClientConstants.SyntheticClientIds.CommunityPatch, Name = "Community Patch", PublisherType = CommunityOutpostConstants.PublisherType, GameType = GameType.ZeroHour, InstallationId = installation.Id };
                            bool forceAttr = cpDecision == GameClientConstants.WizardActionTypes.Update;
                            var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(installation, clientToUse, forceReacquireContent: forceAttr);
                            if (result.Success && result.Data > 0) profilesCreated += result.Data;
                            anyPatchHandled = true;
                        }
                    }

                    // Execute GO Decision
                    if (goDecision != GameClientConstants.WizardActionTypes.Decline && goDecision != GameClientConstants.WizardActionTypes.None)
                    {
                        var goClient = installation.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.GeneralsOnline);
                        if (goClient != null || goDecision == GameClientConstants.WizardActionTypes.Install)
                        {
                            var clientToUse = goClient ?? new GameClient { Id = GameClientConstants.SyntheticClientIds.GeneralsOnline, Name = "GeneralsOnline", PublisherType = PublisherTypeConstants.GeneralsOnline, GameType = GameType.ZeroHour, InstallationId = installation.Id };
                            bool forceAttr = goDecision == GameClientConstants.WizardActionTypes.Update;
                            var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(installation, clientToUse, forceReacquireContent: forceAttr);
                            if (result.Success && result.Data > 0) profilesCreated += result.Data;
                            anyPatchHandled = true;
                        }
                    }

                    // Execute SH Decision
                    if (shDecision != GameClientConstants.WizardActionTypes.Decline && shDecision != GameClientConstants.WizardActionTypes.None)
                    {
                        var shClient = installation.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.TheSuperHackers);
                        if (shClient != null || shDecision == GameClientConstants.WizardActionTypes.Install)
                        {
                            var clientToUse = shClient ?? new GameClient { Id = GameClientConstants.SyntheticClientIds.SuperHackers, Name = "SuperHackers", PublisherType = PublisherTypeConstants.TheSuperHackers, GameType = GameType.ZeroHour, InstallationId = installation.Id };
                            bool forceAttr = shDecision == GameClientConstants.WizardActionTypes.Update;
                            var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(installation, clientToUse, forceReacquireContent: forceAttr);
                            if (result.Success && result.Data > 0) profilesCreated += result.Data;
                            anyPatchHandled = true;
                        }
                    }

                    // Fallback to base game profiles if no patches were handled and no patches were selected globally
                    // If user selected patches globally, we assume they only want those specific patched profiles
                    if (!anyPatchHandled && !anyPatchSelectedGlobally)
                    {
                        logger.LogInformation("No patches selected or found for {InstallationId}, creating base game profiles", installation.Id);
                        foreach (var client in installation.AvailableGameClients.Where(c => !c.IsPublisherClient).ToList())
                        {
                            if (await TryCreateProfileForGameClientAsync(installation, client)) profilesCreated++;
                        }
                    }
                }

                StatusMessage = $"Scan complete. Found {installationsList.Count} installations, created {profilesCreated} profiles";

                notificationService.ShowSuccess(
                    "Scan Complete",
                    $"Created {profilesCreated} profile(s) for your game installations.",
                    autoDismissMs: NotificationDurations.VeryLong);
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
    /// Expands the header and stops the auto-collapse timer (user is interacting).
    /// </summary>
    [RelayCommand]
    private void ExpandHeader()
    {
        IsHeaderExpanded = true;
        _isHovering = true;
        _headerCollapseTimer.Stop();
    }

    /// <summary>
    /// Restarts the auto-collapse timer (user finished interaction).
    /// </summary>
    [RelayCommand]
    private void StartHeaderTimer()
    {
        _isHovering = false;

        if (IsScanning)
        {
            return; // Don't collapse header while scanning
        }

        _headerCollapseTimer.Stop();
        _headerCollapseTimer.Start();
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

            // For publisher clients, handle specially to create all variant profiles
            if (gameClient.IsPublisherClient)
            {
                var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(installation, gameClient);
                if (result.Success && result.Data > 0)
                {
                    // No need to manually refresh - ProfileCreatedMessage handles UI updates
                    return true;
                }

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

            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            // Generate the GameInstallation manifest ID
            // Logic must match GameInstallationService.GenerateAndPoolManifestForGameTypeAsync to ensure ID alignment
            string installationManifestId;
            if (string.IsNullOrEmpty(gameClient.Version) ||
                gameClient.Version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
                gameClient.Version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                gameClient.Version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                // For unknown/auto versions, use the default version for the game type (1.04/1.08)
                // This ensures we match the ID generated during dependency resolution
                var defaultVersion = gameClient.GameType == GameType.ZeroHour
                    ? ManifestConstants.ZeroHourManifestVersion
                    : ManifestConstants.GeneralsManifestVersion;

                var normalizedVersion = GameVersionHelper.NormalizeVersion(defaultVersion);
                installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, normalizedVersion);
            }
            else
            {
                try
                {
                    // Use string overload which handles normalization (e.g. "1.0" -> "100") consistent with ManifestIdGenerator rules
                    installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, gameClient.Version);
                }
                catch (ArgumentException)
                {
                    // If normalization fails (invalid format), fallback to default version
                    var defaultVersion = gameClient.GameType == GameType.ZeroHour
                        ? ManifestConstants.ZeroHourManifestVersion
                        : ManifestConstants.GeneralsManifestVersion;

                    var normalizedVersion = GameVersionHelper.NormalizeVersion(defaultVersion);
                    installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, normalizedVersion);
                }
            }

            // Create enabled content list: GameInstallation manifest + GameClient manifest
            var enabledContentIds = new List<string>
            {
                installationManifestId, // GameInstallation manifest (required for launch validation)
                gameClient.Id,          // GameClient manifest (required for launch validation)
            };

            // Determine assets based on game type using ProfileResourceService
            var gameTypeStr = gameClient.GameType.ToString();
            var iconPath = profileResourceService.GetDefaultIconPath(gameTypeStr);
            var coverPath = profileResourceService.GetDefaultCoverPath(gameTypeStr);

            // Create the profile request using the client manifest ID for GameClientId
            var createRequest = new CreateProfileRequest
            {
                Name = profileName,
                GameInstallationId = installation.Id, // The actual installation GUID
                GameClientId = gameClient.Id, // Client manifest ID
                Description = $"Auto-created profile for {installation.InstallationType} {gameClient.Name}",
                PreferredStrategy = preferredStrategy,
                EnabledContentIds = enabledContentIds, // Both GameInstallation and GameClient manifests
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = iconPath,
                CoverPath = coverPath,
            };

            var profileResult = await profileEditorFacade.CreateProfileWithWorkspaceAsync(createRequest);
            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation("Successfully created profile '{ProfileName}' for {InstallationType} {GameClientName}", profileResult.Data.Name, installation.InstallationType, gameClient.Name);

                // Add profile to UI immediately
                AddProfileToUI(profileResult.Data);

                return true;
            }
            else
            {
                var errors = ManifestHelper.FormatErrors(profileResult.Errors);
                logger.LogWarning("Failed to create profile for {InstallationType} {GameClientName}: {Errors}", installation.InstallationType, gameClient.Name, errors);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profile for {InstallationType} {GameClientName}", installation.InstallationType, gameClient?.Name ?? GameClientConstants.UnknownVersion);
            return false;
        }
    }

    /// <summary>
    /// Checks if a profile already exists for a game client.
    /// Handles special matching for publisher clients (e.g., GeneralsOnline)
    /// when the profile name differs slightly from the detected client name (e.g., "GeneralsOnline" vs "GeneralsOnline 30Hz").
    /// </summary>
    private async Task<bool> ProfileExistsAsync(GameInstallation installation, GameClient gameClient)
    {
        try
        {
            var profileName = $"{installation.InstallationType} {gameClient.Name}";
            var existingProfiles = await gameProfileManager.GetAllProfilesAsync();

            if (existingProfiles.Success && existingProfiles.Data != null)
            {
                // For publisher clients, check if any profile exists for this publisher type
                if (gameClient.IsPublisherClient && !string.IsNullOrEmpty(gameClient.PublisherType))
                {
                    bool publisherProfileExists = existingProfiles.Data.Any(p =>
                        p.GameInstallationId.Equals(installation.Id, StringComparison.OrdinalIgnoreCase) &&
                        p.GameClient != null &&
                        p.GameClient.PublisherType?.Equals(gameClient.PublisherType, StringComparison.OrdinalIgnoreCase) == true);

                    if (publisherProfileExists)
                    {
                        logger.LogDebug(
                            "Profile already exists for publisher {PublisherType} in installation {InstallationId} (looser match)",
                            gameClient.PublisherType,
                            installation.Id);
                        return true;
                    }
                }

                // Standard matching: Check by name AND installation ID
                bool profileExists = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameInstallationId.Equals(installation.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExists) return true;

                // Standard matching: Check by name and game client ID
                bool profileExistsByClient = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameClient != null &&
                    p.GameClient.Id.Equals(gameClient.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExistsByClient) return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if profile exists for {ClientName}", gameClient.Name);
            return false;
        }
    }

    /// <summary>
    /// Adds a newly created profile to the UI immediately (without waiting for full refresh).
    /// </summary>
    private void AddProfileToUI(Core.Models.GameProfile.GameProfile profile)
    {
        try
        {
            // Check if profile already exists in UI
            if (Profiles.OfType<GameProfileItemViewModel>().Any(p => p.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogDebug("Profile {ProfileId} already in UI, skipping add", profile.Id);
                return;
            }

            var iconPath = !string.IsNullOrEmpty(profile.IconPath)
                ? profile.IconPath
                : UriConstants.DefaultIconUri;

            var coverPath = !string.IsNullOrEmpty(profile.CoverPath)
                ? profile.CoverPath
                : iconPath;

            var item = new GameProfileItemViewModel(
                profile.Id,
                profile,
                iconPath,
                coverPath)
            {
                LaunchAction = LaunchProfileAsync,
                EditProfileAction = EditProfile,
                DeleteProfileAction = DeleteProfile,
                CreateShortcutAction = CreateShortcut,
            };

            // Add to collection before the "Add New Profile" button (which is always at the end)
            // If the last item is not AddProfileItemViewModel, just Add
            if (Profiles.Count > 0 && Profiles[^1] is AddProfileItemViewModel)
            {
                Profiles.Insert(Profiles.Count - 1, item);
            }
            else
            {
                Profiles.Add(item);
            }

            logger.LogDebug("Added profile {ProfileName} to UI (Total: {Count})", profile.Name, Profiles.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error adding profile {ProfileId} to UI", profile.Id);
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
                StatusMessage = $"Validating {profile.Name}...";
                ErrorMessage = string.Empty;

                // With CAS hardlinks, profile switching is instant - maps are just symlinks
                logger.LogDebug("[Launch] Launching profile {ProfileName} (ID: {ProfileId})", profile.Name, profile.ProfileId);

                // Normal launch
                await ExecuteLaunchAsync(profile, skipUserDataCleanup: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting launch process for {ProfileName}", profile.Name);
                StatusMessage = $"Error launching {profile.Name}";
                ErrorMessage = ex.Message;
                notificationService.ShowError("Launch Error", $"Error starting launch for {profile.Name}: {ex.Message}");
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
    /// Executes the actual launch operation.
    /// </summary>
    private async Task ExecuteLaunchAsync(GameProfileItemViewModel profile, bool skipUserDataCleanup)
    {
        StatusMessage = $"Launching {profile.Name}...";

        // Show "taking a while" message if many maps are being linked
        if (skipUserDataCleanup && profile.IsLargeMapCount)
        {
            StatusMessage = "Adding maps to profile (this might take a while)...";
            notificationService.ShowInfo("Loading Maps", "Adding many maps to this profile. This may take a moment...", NotificationDurations.Long);
        }

        var launchResult = await profileLauncherFacade.LaunchProfileAsync(profile.ProfileId, skipUserDataCleanup);

        if (launchResult.Success && launchResult.Data != null)
        {
            // Look up the profile in the collection in case it was replaced by an update during launch
            var liveProfile = Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId) ?? profile;

            liveProfile.IsProcessRunning = true;
            liveProfile.ProcessId = launchResult.Data.ProcessInfo.ProcessId;
            liveProfile.ShowUserDataConfirmation = false; // Hide confirmation if it was shown

            // Ensure notifications are sent for binding updates
            liveProfile.NotifyCanLaunchChanged();

            StatusMessage = $"{liveProfile.Name} launched successfully (Process ID: {launchResult.Data.ProcessInfo.ProcessId})";
            notificationService.ShowSuccess("Game Launched", $"{liveProfile.Name} is now running.");
        }
        else
        {
            var errors = string.Join(", ", launchResult.Errors);
            StatusMessage = $"Failed to launch {profile.Name}: {errors}";
            ErrorMessage = errors;
            notificationService.ShowError("Launch Failed", $"Failed to launch {profile.Name}: {errors}");
        }
    }

    /// <summary>
    /// Confirms that user data should be kept and added to the new profile.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmUserDataKeepAsync(GameProfileItemViewModel profile)
    {
        profile.ShowUserDataConfirmation = false;

        if (!await _launchSemaphore.WaitAsync(0))
        {
            StatusMessage = "A profile is already launching...";
            return;
        }

        try
        {
            IsLaunching = true;
            await ExecuteLaunchAsync(profile, skipUserDataCleanup: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during confirmed launch (Keep) for {ProfileName}", profile.Name);
            StatusMessage = $"Error launching {profile.Name}";
            ErrorMessage = ex.Message;
            notificationService.ShowError("Launch Error", $"Error launching {profile.Name}: {ex.Message}");
        }
        finally
        {
            IsLaunching = false;
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// Confirms that user data should be removed (normal switch).
    /// </summary>
    [RelayCommand]
    private async Task ConfirmUserDataRemoveAsync(GameProfileItemViewModel profile)
    {
        profile.ShowUserDataConfirmation = false;

        if (!await _launchSemaphore.WaitAsync(0))
        {
            StatusMessage = "A profile is already launching...";
            return;
        }

        try
        {
            IsLaunching = true;
            await ExecuteLaunchAsync(profile, skipUserDataCleanup: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during confirmed launch (Remove) for {ProfileName}", profile.Name);
            StatusMessage = $"Error launching {profile.Name}";
            ErrorMessage = ex.Message;
            notificationService.ShowError("Launch Error", $"Error launching {profile.Name}: {ex.Message}");
        }
        finally
        {
            IsLaunching = false;
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// Cancels the user data confirmation and stops the launch.
    /// </summary>
    [RelayCommand]
    private void CancelUserDataConfirmation(GameProfileItemViewModel profile)
    {
        profile.ShowUserDataConfirmation = false;
        profile.UserDataSwitchInfo = null;
        StatusMessage = "Launch cancelled";
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
                notificationService.ShowInfo("Game Stopped", $"{profile.Name} has been stopped.");
            }
            else
            {
                var errors = string.Join(", ", stopResult.Errors);
                StatusMessage = $"Failed to stop {profile.Name}: {errors}";
                logger.LogWarning(
                    "Failed to stop profile {ProfileName}: {Errors}",
                    profile.Name,
                    errors);
                notificationService.ShowError("Stop Failed", $"Failed to stop {profile.Name}: {errors}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping profile {ProfileName}", profile.Name);
            StatusMessage = $"Error stopping {profile.Name}";
            notificationService.ShowError("Stop Error", $"An error occurred while stopping {profile.Name}.");
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
        logger.LogInformation("Toggled edit mode to {IsEditMode}", IsEditMode);
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
            logger.LogInformation("Saved profiles in edit mode");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving profiles");
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

        // Show confirmation dialog
        var confirmed = await dialogService.ShowConfirmationAsync(
            "Delete Profile",
            $"Are you sure you want to delete the profile '{profile.Name}'? This action cannot be undone.",
            confirmText: "Delete",
            sessionKey: "DeleteProfileConfirmation");

        if (!confirmed)
        {
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
                logger.LogInformation("Deleted profile {ProfileName}", profile.Name);

                notificationService.ShowSuccess("Profile Deleted", $"Successfully deleted profile '{profile.Name}'.");

                try
                {
                    WeakReferenceMessenger.Default.Send(
                        new ProfileDeletedMessage(profile.ProfileId, profile.Name), 0);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send ProfileDeletedMessage");
                }
            }
            else
            {
                var errors = string.Join(", ", deleteResult.Errors);
                StatusMessage = $"Failed to delete {profile.Name}: {errors}";
                logger.LogWarning("Failed to delete profile {ProfileName}: {Errors}", profile.Name, errors);
                notificationService.ShowError("Delete Failed", $"Failed to delete profile '{profile.Name}': {errors}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting profile {ProfileName}", profile.Name);
            StatusMessage = $"Error deleting {profile.Name}";
            notificationService.ShowError("Delete Error", $"An error occurred while deleting profile '{profile.Name}'.");
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
                var errors = string.Join(", ", loadResult.Errors);
                StatusMessage = $"Failed to load profile: {errors}";
                notificationService.ShowError("Load Failed", $"Failed to load profile '{profile.Name}': {errors}");
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
            logger.LogError(ex, "Error editing profile {ProfileName}", profile.Name);
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
            logger.LogError(ex, "Error creating new profile");
            StatusMessage = "Error creating new profile";
            notificationService.ShowError("Error", "An error occurred while opening the new profile window.");
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
                        logger.LogDebug("Forced UI refresh for profile {ProfileName} at index {Index}", profile.Name, index);
                    }
                }

                StatusMessage = $"Workspace prepared for {profile.Name} at {prepareResult.Data.WorkspacePath}";
                logger.LogInformation("Prepared workspace for profile {ProfileName} at {Path}", profile.Name, prepareResult.Data.WorkspacePath);
                notificationService.ShowSuccess("Workspace Ready", $"Workspace prepared for '{profile.Name}'.");
            }
            else
            {
                var errors = string.Join(", ", prepareResult.Errors);
                StatusMessage = $"Failed to prepare workspace for {profile.Name}: {errors}";
                logger.LogWarning("Failed to prepare workspace for profile {ProfileName}: {Errors}", profile.Name, errors);
                notificationService.ShowError("Workspace Failed", $"Failed to prepare workspace for '{profile.Name}': {errors}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error preparing workspace for profile {ProfileName}", profile.Name);
            StatusMessage = $"Error preparing workspace for {profile.Name}";
            notificationService.ShowError("Workspace Error", $"An error occurred while preparing workspace for '{profile.Name}'.");
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
                logger.LogInformation("Created desktop shortcut for profile {ProfileName} at {Path}", profile.Name, result.Data);
                notificationService.ShowSuccess("Shortcut Created", $"Desktop shortcut created for '{profile.Name}'.");
            }
            else
            {
                StatusMessage = $"Failed to create shortcut: {string.Join(", ", result.Errors)}";
                logger.LogWarning("Failed to create shortcut for profile {ProfileName}: {Errors}", profile.Name, string.Join(", ", result.Errors));
                notificationService.ShowError("Shortcut Failed", $"Failed to create shortcut for '{profile.Name}': {string.Join(", ", result.Errors)}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating shortcut for profile {ProfileName}", profile.Name);
            StatusMessage = $"Error creating shortcut for {profile.Name}";
            notificationService.ShowError("Shortcut Error", $"An error occurred while creating shortcut for '{profile.Name}'.");
        }
    }

    /// <summary>
    /// Toggles the Steam launch mode for a profile and updates the manifest on disk.
    /// </summary>
    /// <param name="profile">The profile to update.</param>
    [RelayCommand]
    private async Task ToggleSteamLaunch(GameProfileItemViewModel profile)
    {
        try
        {
            if (profile.Profile is Core.Models.GameProfile.GameProfile gameProfile && !string.IsNullOrEmpty(gameProfile.GameClient?.Id))
            {
                StatusMessage = $"Updating launch mode for {profile.Name}...";

                // Update the persisted profile
                var updateRequest = new Core.Models.GameProfile.UpdateProfileRequest
                {
                    UseSteamLaunch = profile.UseSteamLaunch,
                };
                await gameProfileManager.UpdateProfileAsync(profile.ProfileId, updateRequest);

                // Patch all enabled manifests on disk immediately
                foreach (var contentId in gameProfile.EnabledContentIds)
                {
                    await steamManifestPatcher.PatchManifestAsync(contentId, profile.UseSteamLaunch);
                }

                StatusMessage = $"Steam launch {(profile.UseSteamLaunch ? "enabled" : "disabled")} for {profile.Name}";
                logger.LogInformation("Toggled Steam launch to {UseSteam} for profile {ProfileName}", profile.UseSteamLaunch, profile.Name);
                notificationService.ShowInfo("Steam Integration", $"Steam launch {(profile.UseSteamLaunch ? "enabled" : "disabled")} for '{profile.Name}'.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling Steam launch for {ProfileName}", profile.Name);
            StatusMessage = "Error updating launch mode";
            notificationService.ShowError("Steam Integration Error", $"Failed to update Steam launch for '{profile.Name}'.");

            // Revert UI if failed
            profile.UseSteamLaunch = !profile.UseSteamLaunch;
        }
    }

    /// <summary>
    /// Handles the process exited event to update profile state when a game exits.
    /// </summary>
    private void OnProcessExited(object? sender, Core.Models.Events.GameProcessExitedEventArgs e)
    {
        try
        {
            logger.LogInformation("Game process {ProcessId} exited with code {ExitCode}", e.ProcessId, e.ExitCode);

            // Find the profile that was running this process
            var profile = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProcessId == e.ProcessId);
            if (profile != null)
            {
                profile.IsProcessRunning = false;
                profile.ProcessId = 0;
                logger.LogInformation("Updated profile {ProfileName} - process no longer running", profile.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling process exit event for process {ProcessId}", e.ProcessId);
        }
    }

    /// <summary>
    /// Prompts the user to manually select a game directory when auto-detection fails.
    /// </summary>
    /// <returns>A GameInstallation if user selects a valid directory, otherwise null.</returns>
    private async Task<GameInstallation?> PromptForManualGameDirectoryAsync()
    {
        try
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                logger.LogWarning("Cannot show folder picker - main window not found");
                return null;
            }

            var folderPickerOptions = new FolderPickerOpenOptions
            {
                Title = $"Select {GameClientConstants.ZeroHourFullName} Installation Directory",
                AllowMultiple = false,
            };

            var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(folderPickerOptions);

            if (result.Count == 0)
            {
                return null; // User cancelled
            }

            var selectedPath = result[0].Path.LocalPath;
            logger.LogInformation("User selected directory: {Path}", selectedPath);

            // Validate the selected directory contains game executables
            string[] zeroHourExecutables =
            [
                GameClientConstants.ZeroHourExecutable,
                GameClientConstants.GeneralsExecutable,
                GameClientConstants.SuperHackersZeroHourExecutable,
            ];

            string[] generalsExecutables =
            [
                GameClientConstants.GeneralsExecutable,
                GameClientConstants.SuperHackersGeneralsExecutable,
            ];

            bool hasZeroHour = zeroHourExecutables.Any(exe => File.Exists(Path.Combine(selectedPath, exe)));
            bool hasGenerals = generalsExecutables.Any(exe => File.Exists(Path.Combine(selectedPath, exe)));

            if (hasZeroHour || hasGenerals)
            {
                // Selected directory is the game directory
                var installation = new GameInstallation(
                    selectedPath,
                    GameInstallationType.Retail,
                    null);

                installation.SetPaths(
                    hasGenerals ? selectedPath : null,
                    hasZeroHour ? selectedPath : null);

                logger.LogInformation(
                    "Created manual Retail installation from selected directory: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                    hasGenerals,
                    hasZeroHour);

                return installation;
            }

            // Check if it's a parent directory with subdirectories
            var generalsSubdir = Path.Combine(selectedPath, GameClientConstants.GeneralsDirectoryName);
            var zeroHourSubdir = Path.Combine(selectedPath, GameClientConstants.ZeroHourDirectoryName);

            if (Directory.Exists(generalsSubdir))
            {
                hasGenerals = generalsExecutables.Any(exe => File.Exists(Path.Combine(generalsSubdir, exe)));
            }

            if (Directory.Exists(zeroHourSubdir))
            {
                hasZeroHour = zeroHourExecutables.Any(exe => File.Exists(Path.Combine(zeroHourSubdir, exe)));
            }

            if (hasGenerals || hasZeroHour)
            {
                // Use parent directory as base path
                var installation = new GameInstallation(
                    selectedPath,
                    GameInstallationType.Retail,
                    null);

                installation.SetPaths(
                    hasGenerals ? generalsSubdir : null,
                    hasZeroHour ? zeroHourSubdir : null);

                logger.LogInformation(
                    "Created manual Retail installation from parent directory: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                    hasGenerals,
                    hasZeroHour);

                return installation;
            }

            logger.LogWarning("Selected directory does not contain valid game executables: {Path}", selectedPath);
            notificationService.ShowWarning(
                "Invalid Directory",
                "The selected directory does not contain valid game executables.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during manual directory selection");
            notificationService.ShowError(
                "Error",
                $"Failed to process selected directory: {ex.Message}");
            return null;
        }
    }
}
