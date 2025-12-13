using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for managing game profile settings, including content selection and configuration.
/// </summary>
public partial class GameProfileSettingsViewModel(
    IGameProfileManager? gameProfileManager,
    IGameSettingsService? gameSettingsService,
    IConfigurationProviderService? configurationProvider,
    IProfileContentLoader? profileContentLoader,
    ILogger<GameProfileSettingsViewModel>? logger,
    ILogger<GameSettingsViewModel>? gameSettingsLogger) : ViewModelBase
{
    private readonly ILogger<GameProfileSettingsViewModel> _logger = logger ?? NullLogger<GameProfileSettingsViewModel>.Instance;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _colorValue = "#1976D2";

    // Remove [ObservableProperty] for SelectedContentType to implement custom setter
    private ContentType _selectedContentType = ContentType.GameInstallation;

    /// <summary>
    /// Gets or sets the selected content type for filtering available content.
    /// </summary>
    public ContentType SelectedContentType
    {
        get => _selectedContentType;
        set
        {
            if (SetProperty(ref _selectedContentType, value))
            {
                // Property updates immediately - UI shows selection right away
                // Then fire async load in background
                _ = OnContentTypeChangedAsync();
            }
        }
    }

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableContent = new();

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableGameInstallations = new();

    [ObservableProperty]
    private ContentDisplayItem? _selectedGameInstallation;

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _enabledContent = new();

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _loadingError;

    [ObservableProperty]
    private WorkspaceStrategy _selectedWorkspaceStrategy = WorkspaceStrategy.SymlinkOnly;

    // Track the original workspace strategy when loading a profile to detect changes
    private WorkspaceStrategy? _originalWorkspaceStrategy;

    [ObservableProperty]
    private string _commandLineArguments = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableCovers = new();

    [ObservableProperty]
    private ProfileInfoItem? _selectedCover;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableGameClients = new();

    [ObservableProperty]
    private ProfileInfoItem? _selectedClient;

    [ObservableProperty]
    private string _formattedSize = string.Empty;

    [ObservableProperty]
    private string _buildDate = string.Empty;

    [ObservableProperty]
    private string _sourceType = string.Empty;

    [ObservableProperty]
    private string _shortcutPath = string.Empty;

    [ObservableProperty]
    private bool _isShortcutPathValid = true;

    [ObservableProperty]
    private string _shortcutStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _useProfileIcon;

    [ObservableProperty]
    private bool _shortcutRunAsAdmin;

    [ObservableProperty]
    private string _shortcutDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _profileInfos = new();

    [ObservableProperty]
    private ProfileInfoItem? _selectedProfileInfo;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableExecutables = new();

    [ObservableProperty]
    private ProfileInfoItem? _selectedExecutable;

    [ObservableProperty]
    private bool _isExecutableValid = true;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableDataPaths = new();

    [ObservableProperty]
    private ProfileInfoItem? _selectedDataPath;

    [ObservableProperty]
    private bool _isDataPathValid = true;

    [ObservableProperty]
    private bool _runAsAdmin;

    [ObservableProperty]
    private bool _canLaunchGame = true;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _sourceTypeName = string.Empty;

    [ObservableProperty]
    private string _gameType = string.Empty;

    [ObservableProperty]
    private string _installPath = string.Empty;

    private string? _currentProfileId;
    private bool _isLoadingContent; // Guard flag to prevent cascading EnableContent calls

    /// <summary>
    /// Event that is raised when the window should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets available content types for selection.
    /// </summary>
    public static ContentType[] AvailableContentTypes { get; } =
    {
        ContentType.GameInstallation,
        ContentType.GameClient,
        ContentType.Mod,
        ContentType.MapPack,
        ContentType.Patch,
    };

    /// <summary>
    /// Gets available workspace strategies for selection.
    /// </summary>
    public static WorkspaceStrategy[] AvailableWorkspaceStrategies { get; } =
    {
        WorkspaceStrategy.SymlinkOnly,
        WorkspaceStrategy.HybridCopySymlink,
        WorkspaceStrategy.HardLink,
        WorkspaceStrategy.FullCopy,
    };

    /// <summary>
    /// Gets the Game Settings ViewModel for the third tab.
    /// </summary>
    public GameSettingsViewModel GameSettingsViewModel { get; } = new(gameSettingsService!, gameSettingsLogger!);

    /// <summary>
    /// Initializes the view model for creating a new profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeForNewProfileAsync()
    {
        try
        {
            IsInitializing = true;
            LoadingError = false;
            StatusMessage = "Loading available content...";

            _currentProfileId = null;
            Name = "New Profile";
            Description = "A new game profile";
            ColorValue = "#1976D2";
            SelectedWorkspaceStrategy = GetDefaultWorkspaceStrategy();
            SelectedContentType = ContentType.GameInstallation;

            EnabledContent.Clear();

            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();

            // Set the first game installation as selected (for UI convenience), but don't auto-enable it
            if (AvailableGameInstallations.Any())
            {
                SelectedGameInstallation = AvailableGameInstallations.First();
                _logger.LogInformation("Pre-selected first GameInstallation for UI: {ContentName}", SelectedGameInstallation.DisplayName);
            }

            // Initialize game settings with defaults for new profile
            if (GameSettingsViewModel != null)
            {
                await GameSettingsViewModel.InitializeForProfileAsync(null, null);
            }

            StatusMessage = $"Found {AvailableGameInstallations.Count} installations and {AvailableContent.Count} content items";
            _logger.LogInformation(
                "Initialized new profile creation with {InstallationCount} installations and {ContentCount} content items",
                AvailableGameInstallations.Count,
                AvailableContent.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing new profile");
            StatusMessage = "Error loading content";
            LoadingError = true;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Initializes the view model for editing an existing profile.
    /// </summary>
    /// <param name="profileId">The profile ID to edit.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeForProfileAsync(string profileId)
    {
        try
        {
            IsInitializing = true;
            LoadingError = false;
            StatusMessage = "Loading profile...";

            _currentProfileId = profileId;
            _logger.LogInformation("InitializeForProfileAsync called with profileId: {ProfileId}", profileId);

            if (gameProfileManager == null)
            {
                throw new InvalidOperationException("GameProfileManager service is not available.");
            }

            // Load the existing profile
            var profileResult = await gameProfileManager.GetProfileAsync(profileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                _logger.LogWarning("Failed to load profile {ProfileId}: {Errors}", profileId, string.Join(", ", profileResult.Errors));
                StatusMessage = "Failed to load profile";
                LoadingError = true;
                return;
            }

            var profile = profileResult.Data;
            _logger.LogInformation("Loaded profile: {ProfileName}, EnabledContentIds count: {Count}", profile.Name, profile.EnabledContentIds?.Count ?? 0);

            Name = profile.Name;
            Description = profile.Description ?? string.Empty;
            ColorValue = profile.ThemeColor ?? "#1976D2";
            SelectedWorkspaceStrategy = profile.WorkspaceStrategy;
            _originalWorkspaceStrategy = profile.WorkspaceStrategy; // Track original strategy
            CommandLineArguments = profile.CommandLineArguments ?? string.Empty;

            // Load game settings for this profile
            if (GameSettingsViewModel != null)
            {
                await GameSettingsViewModel.InitializeForProfileAsync(profileId, profile);
            }

            // If the profile has no custom game settings, save the defaults from Options.ini
            if (GameSettingsViewModel != null && !profile.HasCustomSettings())
            {
                _logger.LogInformation("Profile {ProfileId} has no custom settings, saving defaults from Options.ini", profileId);
                var gameSettings = GameSettingsViewModel.GetProfileSettings();
                var updateRequest = new UpdateProfileRequest();
                PopulateGameSettings(updateRequest, gameSettings);

                var updateResult = await gameProfileManager.UpdateProfileAsync(profileId, updateRequest);
                if (updateResult.Success)
                {
                    _logger.LogInformation("Saved default game settings for profile {ProfileId}", profileId);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to save default game settings for profile {ProfileId}: {Errors}",
                        profileId,
                        string.Join(", ", updateResult.Errors));
                }
            }

            // Load enabled content for this profile
            _logger.LogInformation("About to call LoadEnabledContentForProfileAsync for profile: {ProfileName}", profile.Name);
            await LoadEnabledContentForProfileAsync(profile);
            _logger.LogInformation("After LoadEnabledContentForProfileAsync: EnabledContent count = {Count}", EnabledContent.Count);

            _logger.LogInformation("Loaded profile {ProfileName} for editing", profile.Name);

            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();

            // Auto-select the GameInstallation content type filter if there are enabled GameInstallations
            var hasGameInstallation = EnabledContent.Any(c => c.ContentType == ContentType.GameInstallation);
            if (hasGameInstallation)
            {
                SelectedContentType = ContentType.GameInstallation;
                _logger.LogInformation("Auto-selected GameInstallation content type filter for editing");

                var enabledInstallation = EnabledContent.FirstOrDefault(c => c.ContentType == ContentType.GameInstallation);
                if (enabledInstallation != null)
                {
                    // Find the matching item in AvailableGameInstallations
                    SelectedGameInstallation = AvailableGameInstallations
                        .FirstOrDefault(a => a.ManifestId.Value == enabledInstallation.ManifestId.Value)
                        ?? enabledInstallation;

                    _logger.LogInformation(
                        "Set SelectedGameInstallation to {DisplayName} from existing profile",
                        SelectedGameInstallation.DisplayName);
                }
            }

            StatusMessage = $"Profile loaded with {EnabledContent.Count} enabled content items";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing profile {ProfileId}", profileId);
            StatusMessage = "Error loading profile";
            LoadingError = true;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Populates game settings into an UpdateProfileRequest.
    /// </summary>
    /// <param name="request">The update request to populate.</param>
    /// <param name="gameSettings">The game settings to apply, or null to skip.</param>
    private static void PopulateGameSettings(
        UpdateProfileRequest request,
        UpdateProfileRequest? gameSettings)
    {
        if (gameSettings == null)
            return;

        request.VideoResolutionWidth = gameSettings.VideoResolutionWidth;
        request.VideoResolutionHeight = gameSettings.VideoResolutionHeight;
        request.VideoWindowed = gameSettings.VideoWindowed;
        request.VideoTextureQuality = gameSettings.VideoTextureQuality;
        request.EnableVideoShadows = gameSettings.EnableVideoShadows;
        request.VideoParticleEffects = gameSettings.VideoParticleEffects;
        request.VideoExtraAnimations = gameSettings.VideoExtraAnimations;
        request.VideoBuildingAnimations = gameSettings.VideoBuildingAnimations;
        request.VideoGamma = gameSettings.VideoGamma;
        request.AudioSoundVolume = gameSettings.AudioSoundVolume;
        request.AudioThreeDSoundVolume = gameSettings.AudioThreeDSoundVolume;
        request.AudioSpeechVolume = gameSettings.AudioSpeechVolume;
        request.AudioMusicVolume = gameSettings.AudioMusicVolume;
        request.AudioEnabled = gameSettings.AudioEnabled;
        request.AudioNumSounds = gameSettings.AudioNumSounds;
    }

    /// <summary>
    /// Gets the default workspace strategy from configuration.
    /// </summary>
    private WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        return configurationProvider?.GetDefaultWorkspaceStrategy() ?? WorkspaceStrategy.SymlinkOnly;
    }

    /// <summary>
    /// Loads available content based on the selected content type.
    /// </summary>
    [RelayCommand]
    private async Task LoadAvailableContentAsync()
    {
        try
        {
            if (profileContentLoader == null)
            {
                StatusMessage = "Profile content loader service not available";
                _logger.LogWarning("Profile content loader service not available");
                return;
            }

            _isLoadingContent = true;
            StatusMessage = "Loading content...";
            AvailableContent.Clear();

            // Get enabled content IDs for marking items as enabled
            var enabledContentIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();

            // Convert AvailableGameInstallations to Core items for the service
            var coreAvailableInstallations = new List<Core.Models.Content.ContentDisplayItem>();
            foreach (var vmItem in AvailableGameInstallations)
            {
                coreAvailableInstallations.Add(new Core.Models.Content.ContentDisplayItem
                {
                    Id = vmItem.ManifestId.Value,
                    ManifestId = vmItem.ManifestId.Value,
                    DisplayName = vmItem.DisplayName,
                    ContentType = vmItem.ContentType,
                    GameType = vmItem.GameType,
                    InstallationType = vmItem.InstallationType,
                    Publisher = vmItem.Publisher ?? string.Empty,
                    Version = vmItem.Version ?? string.Empty,
                    SourceId = vmItem.SourceId ?? string.Empty,
                    GameClientId = vmItem.GameClientId ?? string.Empty,
                    IsEnabled = vmItem.IsEnabled,
                });
            }

            var coreItems = await profileContentLoader.LoadAvailableContentAsync(
                SelectedContentType,
                new ObservableCollection<Core.Models.Content.ContentDisplayItem>(coreAvailableInstallations),
                enabledContentIds);

            // Convert Core items to ViewModel items, excluding already-enabled content
            foreach (var coreItem in coreItems)
            {
                // Skip items that are already in the EnabledContent list
                if (enabledContentIds.Contains(coreItem.ManifestId))
                {
                    continue;
                }

                var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                AvailableContent.Add(viewModelItem);
            }

            StatusMessage = $"Loaded {AvailableContent.Count} {SelectedContentType} items";
            _logger.LogInformation("Loaded {Count} content items for content type {ContentType}", AvailableContent.Count, SelectedContentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available content");
            StatusMessage = "Error loading content";
        }
        finally
        {
            _isLoadingContent = false;
        }
    }

    /// <summary>
    /// Loads available game installations from actual detected installations (not manifests).
    /// Creates entries for each available GameClient within each installation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task LoadAvailableGameInstallationsAsync()
    {
        try
        {
            AvailableGameInstallations.Clear();

            if (profileContentLoader == null)
            {
                _logger.LogWarning("Profile content loader service not available");
                return;
            }

            var coreItems = await profileContentLoader.LoadAvailableGameInstallationsAsync();

            // Convert Core.ContentDisplayItem to ViewModel items
            foreach (var coreItem in coreItems)
            {
                var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                AvailableGameInstallations.Add(viewModelItem);
            }

            // Select the first installation if available
            if (AvailableGameInstallations.Any() && SelectedGameInstallation == null)
            {
                SelectedGameInstallation = AvailableGameInstallations.First();
            }

            _logger.LogInformation(
                "Loaded {Count} game installation options",
                AvailableGameInstallations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available game installations");
        }
    }

    /// <summary>
    /// Loads enabled content for a specific profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task LoadEnabledContentForProfileAsync(GameProfile profile)
    {
        try
        {
            EnabledContent.Clear();

            if (profileContentLoader == null)
            {
                _logger.LogWarning("Profile content loader service not available");
                return;
            }

            var coreItems = await profileContentLoader.LoadEnabledContentForProfileAsync(profile);

            // Convert Core items to ViewModel items
            foreach (var coreItem in coreItems)
            {
                var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                EnabledContent.Add(viewModelItem);
                viewModelItem.IsEnabled = true; // Ensure enabled status
            }

            _logger.LogInformation("Loaded {Count} enabled content items for profile {ProfileName}", EnabledContent.Count, profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading enabled content for profile");
        }
    }

    /// <summary>
    /// Enables the specified content item for the profile.
    /// This is called with CommandParameter from the UI.
    /// </summary>
    /// <param name="contentItem">The content item to enable.</param>
    [RelayCommand]
    private void EnableContent(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger.LogWarning("EnableContent: contentItem parameter is NULL");
            return;
        }

        // Prevent cascading calls during content loading
        if (_isLoadingContent)
        {
            _logger.LogDebug("EnableContent: Blocked during content loading (guard flag set) - {DisplayName}", contentItem.DisplayName);
            return;
        }

        _logger.LogInformation(
            "EnableContent called with: {DisplayName} (ManifestId: {ManifestId}, SourceId: {SourceId}, GameClientId: {GameClientId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value,
            contentItem.SourceId ?? "NULL",
            contentItem.GameClientId ?? "NULL");

        if (contentItem.IsEnabled)
        {
            StatusMessage = "Content already enabled";
            _logger.LogWarning("EnableContent: {DisplayName} is already marked as enabled", contentItem.DisplayName);
            return;
        }

        // Check if content is already enabled by manifest ID
        var alreadyEnabled = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (alreadyEnabled != null)
        {
            StatusMessage = "Content is already enabled";
            _logger.LogWarning(
                "EnableContent: ManifestId {ManifestId} is already in EnabledContent as {DisplayName}",
                contentItem.ManifestId.Value,
                alreadyEnabled.DisplayName);
            return;
        }

        if (contentItem.ContentType == ContentType.GameInstallation || contentItem.ContentType == ContentType.GameClient)
        {
            // Disable any existing items of the same type (enforce cardinality of 1)
            var existingItems = EnabledContent.Where(e => e.ContentType == contentItem.ContentType).ToList();
            foreach (var existing in existingItems)
            {
                existing.IsEnabled = false;
                EnabledContent.Remove(existing);

                // Re-add to AvailableContent if it matches the current filter
                if (existing.ContentType == SelectedContentType)
                {
                    var alreadyInAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == existing.ManifestId.Value);
                    if (alreadyInAvailable == null)
                    {
                        var reAddedItem = new ContentDisplayItem
                        {
                            ManifestId = existing.ManifestId,
                            DisplayName = existing.DisplayName,
                            ContentType = existing.ContentType,
                            GameType = existing.GameType,
                            InstallationType = existing.InstallationType,
                            Publisher = existing.Publisher,
                            IsEnabled = false,
                            SourceId = existing.SourceId,
                            GameClientId = existing.GameClientId,
                            Version = existing.Version,
                        };
                        AvailableContent.Add(reAddedItem);
                    }
                    else
                    {
                        alreadyInAvailable.IsEnabled = false;
                    }
                }

                _logger.LogInformation(
                    "Disabled existing {ContentType}: {DisplayName} (enforcing cardinality of 1)",
                    existing.ContentType,
                    existing.DisplayName);
            }
        }

        // Add to enabled content
        contentItem.IsEnabled = true;
        EnabledContent.Add(contentItem);

        // Remove from AvailableContent list since it's now enabled
        var itemToRemoveFromAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
        if (itemToRemoveFromAvailable != null)
        {
            AvailableContent.Remove(itemToRemoveFromAvailable);
        }

        if (contentItem.ContentType == ContentType.GameInstallation)
        {
            SelectedGameInstallation = contentItem;
            _logger.LogInformation(
                "Updated SelectedGameInstallation to {DisplayName} (SourceId={SourceId}, GameClientId={GameClientId})",
                contentItem.DisplayName,
                contentItem.SourceId,
                contentItem.GameClientId);
        }

        StatusMessage = $"Enabled {contentItem.DisplayName}";
        _logger.LogInformation("Enabled content {ContentName} for profile", contentItem.DisplayName);
    }

    /// <summary>
    /// Disables the specified content item.
    /// </summary>
    /// <param name="contentItem">The content item to disable.</param>
    [RelayCommand]
    private void DisableContent(ContentDisplayItem contentItem)
    {
        if (contentItem == null)
        {
            return;
        }

        // Remove from enabled content
        var itemToRemove = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (itemToRemove != null)
        {
            itemToRemove.IsEnabled = false;
            EnabledContent.Remove(itemToRemove);
        }

        // If the disabled content matches the current content type filter, add it back to AvailableContent
        if (contentItem.ContentType == SelectedContentType)
        {
            var alreadyInAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
            if (alreadyInAvailable == null)
            {
                // Create a new instance with IsEnabled = false
                var availableItem = new ContentDisplayItem
                {
                    ManifestId = contentItem.ManifestId,
                    DisplayName = contentItem.DisplayName,
                    ContentType = contentItem.ContentType,
                    GameType = contentItem.GameType,
                    InstallationType = contentItem.InstallationType,
                    Publisher = contentItem.Publisher,
                    IsEnabled = false,
                    SourceId = contentItem.SourceId,
                    GameClientId = contentItem.GameClientId,
                    Version = contentItem.Version,
                };
                AvailableContent.Add(availableItem);
            }
            else
            {
                alreadyInAvailable.IsEnabled = false;
            }
        }

        if (contentItem.ContentType == ContentType.GameInstallation &&
            SelectedGameInstallation?.ManifestId.Value == contentItem.ManifestId.Value)
        {
            // Try to find another enabled GameInstallation
            var anotherEnabledInstallation = EnabledContent.FirstOrDefault(c => c.ContentType == ContentType.GameInstallation && c.IsEnabled);

            if (anotherEnabledInstallation != null)
            {
                SelectedGameInstallation = anotherEnabledInstallation;
                _logger.LogInformation(
                    "Switched SelectedGameInstallation to {DisplayName} after disabling previous selection",
                    anotherEnabledInstallation.DisplayName);
            }
            else
            {
                // No GameInstallation enabled - try to select from available list
                SelectedGameInstallation = AvailableGameInstallations.FirstOrDefault();
                _logger.LogWarning(
                    "No GameInstallation enabled - reset to first available: {DisplayName}",
                    SelectedGameInstallation?.DisplayName ?? "None");
            }
        }

        StatusMessage = $"Disabled {contentItem.DisplayName}";
        _logger.LogInformation("Disabled content {ContentName} for profile", contentItem.DisplayName);
    }

    /// <summary>
    /// Saves the profile.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsSaving = true;
            StatusMessage = "Saving profile...";

            if (gameProfileManager == null)
            {
                StatusMessage = "Profile manager not available";
                return;
            }

            if (SelectedGameInstallation == null)
            {
                StatusMessage = "Please select a game installation";
                return;
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                StatusMessage = "Please enter a profile name";
                return;
            }

            // Validate that GameInstallation content is enabled
            var hasGameInstallation = EnabledContent.Any(c => c.ContentType == ContentType.GameInstallation && c.IsEnabled);
            if (!hasGameInstallation)
            {
                StatusMessage = "Error: A Game Installation must be enabled for the profile to be launchable.";
                _logger.LogWarning("Profile save blocked: No GameInstallation content enabled");
                return;
            }

            // Build enabled content IDs from all enabled content
            var enabledContentIds = EnabledContent.Where(c => c.IsEnabled).Select(c => c.ManifestId.Value).ToList();

            _logger.LogInformation(
                "Profile will be created/updated with {Count} enabled content items: {ContentIds}",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            if (string.IsNullOrEmpty(_currentProfileId))
            {
                // Create new profile
                // Auto-enable GameClient ONLY if no GameClient content is already enabled
                var hasGameClientEnabled = EnabledContent.Any(c => c.IsEnabled && c.ContentType == ContentType.GameClient);
                if (!hasGameClientEnabled &&
                    !string.IsNullOrEmpty(SelectedGameInstallation.GameClientId) &&
                    !enabledContentIds.Contains(SelectedGameInstallation.GameClientId))
                {
                    enabledContentIds.Add(SelectedGameInstallation.GameClientId);
                    _logger.LogInformation("Auto-enabled default GameClient content: {GameClientId}", SelectedGameInstallation.GameClientId);
                }
                else if (hasGameClientEnabled)
                {
                    _logger.LogInformation("Skipping auto-enable GameClient - user has already selected a GameClient");
                }

                var createRequest = new CreateProfileRequest
                {
                    Name = Name,
                    Description = Description,
                    GameInstallationId = SelectedGameInstallation.SourceId,
                    GameClientId = SelectedGameInstallation.GameClientId,
                    PreferredStrategy = SelectedWorkspaceStrategy,
                    EnabledContentIds = enabledContentIds,
                    CommandLineArguments = CommandLineArguments,
                };

                var result = await gameProfileManager.CreateProfileAsync(createRequest);
                if (result.Success)
                {
                    StatusMessage = "Profile created successfully";
                    _logger.LogInformation("Created new profile {ProfileName} with {ContentCount} enabled content items", Name, enabledContentIds.Count);

                    // Close the window after a brief delay
                    await Task.Delay(1000);
                    ExecuteCancel();
                }
                else
                {
                    StatusMessage = $"Failed to create profile: {string.Join(", ", result.Errors)}";
                    _logger.LogWarning("Failed to create profile: {Errors}", string.Join(", ", result.Errors));
                }
            }
            else
            {
                // Update existing profile
                var gameSettings = GameSettingsViewModel?.GetProfileSettings();

                var updateRequest = new UpdateProfileRequest
                {
                    Name = Name,
                    Description = Description,
                    ThemeColor = ColorValue,
                    GameInstallationId = SelectedGameInstallation?.SourceId, // Update installation ID when user changes installation

                    // Only update strategy if it was actually changed from the original
                    PreferredStrategy = _originalWorkspaceStrategy.HasValue && SelectedWorkspaceStrategy != _originalWorkspaceStrategy.Value
                        ? SelectedWorkspaceStrategy
                        : null,
                    EnabledContentIds = enabledContentIds,
                    CommandLineArguments = CommandLineArguments,
                };

                PopulateGameSettings(updateRequest, gameSettings);

                var result = await gameProfileManager.UpdateProfileAsync(_currentProfileId, updateRequest);
                if (result.Success)
                {
                    StatusMessage = "Profile updated successfully";
                    _logger.LogInformation("Updated profile {ProfileId} with {ContentCount} enabled content items", _currentProfileId, enabledContentIds.Count);

                    // Close the window after a brief delay
                    await Task.Delay(1000);
                    ExecuteCancel();
                }
                else
                {
                    StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}";
                    _logger.LogWarning("Failed to update profile {ProfileId}: {Errors}", _currentProfileId, string.Join(", ", result.Errors));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile");
            StatusMessage = "Error saving profile";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Randomizes the profile color from a predefined set of colors.
    /// </summary>
    [RelayCommand]
    private void RandomizeColor()
    {
        var colors = new List<string>
        {
            "#1976D2", "#388E3C", "#FBC02D", "#FF5722", "#7B1FA2",
            "#D32F2F", "#0097A7", "#689F38", "#AFB42B", "#0288D1",
            "#C2185B", "#512DA8",
        };

        var random = new Random();
        ColorValue = colors[random.Next(colors.Count)];
        StatusMessage = $"Color randomized to {ColorValue}";
        _logger.LogInformation("Randomized profile color to {ColorValue}", ColorValue);
    }

    /// <summary>
    /// Selects a specific theme color for the profile.
    /// </summary>
    /// <param name="color">The color to select.</param>
    [RelayCommand]
    private void SelectThemeColor(string? color)
    {
        if (!string.IsNullOrEmpty(color))
        {
            ColorValue = color;
            StatusMessage = $"Selected theme color {color}";
            _logger.LogInformation("Selected theme color {ColorValue}", color);
        }
        else
        {
            StatusMessage = "Invalid color selected";
            _logger.LogWarning("Invalid color parameter passed to SelectThemeColor");
        }
    }

    /// <summary>
    /// Browses for a custom cover image.
    /// </summary>
    [RelayCommand]
    private void BrowseCustomCover()
    {
        // TODO: Implement file dialog for selecting cover image
        StatusMessage = "Browse custom cover: TODO - Implement file dialog";
        _logger.LogInformation("BrowseCustomCoverCommand executed");
    }

    /// <summary>
    /// Selects a cover from available options.
    /// </summary>
    [RelayCommand]
    private void SelectCover()
    {
        // TODO: Implement cover selection logic
        StatusMessage = "Select cover: TODO - Implement selection";
        _logger.LogInformation("SelectCoverCommand executed");
    }

    /// <summary>
    /// Browses for a shortcut path.
    /// </summary>
    [RelayCommand]
    private void BrowseShortcutPath()
    {
        // TODO: Implement file dialog for shortcut path
        StatusMessage = "Browse shortcut path: TODO - Implement file dialog";
        _logger.LogInformation("BrowseShortcutPathCommand executed");
    }

    /// <summary>
    /// Selects an icon file.
    /// </summary>
    [RelayCommand]
    private void SelectIcon()
    {
        // TODO: Implement file dialog for icon
        StatusMessage = "Select icon: TODO - Implement file dialog";
        _logger.LogInformation("SelectIconCommand executed");
    }

    /// <summary>
    /// Changes the selected content type filter.
    /// </summary>
    /// <param name="contentType">The content type to filter by.</param>
    [RelayCommand]
    private void SelectContentTypeFilter(ContentType? contentType)
    {
        if (contentType.HasValue && contentType.Value != SelectedContentType)
        {
            SelectedContentType = contentType.Value;
            _logger.LogInformation("Content type filter changed to {ContentType}", contentType.Value);
        }
    }

    /// <summary>
    /// Cancels the operation and closes the window.
    /// </summary>
    [RelayCommand]
    private void ExecuteCancel()
    {
        StatusMessage = "Cancelled";
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when the selected content type changes.
    /// </summary>
    private async Task OnContentTypeChangedAsync()
    {
        await LoadAvailableContentAsync();
    }

    /// <summary>
    /// Converts a Core ContentDisplayItem to the ViewModel's ContentDisplayItem.
    /// </summary>
    /// <param name="coreItem">The core content display item.</param>
    /// <returns>A ViewModel content display item.</returns>
    private ContentDisplayItem ConvertToViewModelContentDisplayItem(Core.Models.Content.ContentDisplayItem coreItem)
    {
        return new ContentDisplayItem
        {
            ManifestId = ManifestId.Create(coreItem.ManifestId),
            DisplayName = coreItem.DisplayName,
            ContentType = coreItem.ContentType,
            GameType = coreItem.GameType,
            InstallationType = coreItem.InstallationType,
            Publisher = coreItem.Publisher,
            Version = coreItem.Version,
            SourceId = coreItem.SourceId,
            GameClientId = coreItem.GameClientId,
            IsEnabled = coreItem.IsEnabled,
        };
    }

    /// <summary>
    /// Converts a collection of Core ContentDisplayItems to ViewModel ContentDisplayItems.
    /// </summary>
    /// <param name="coreItems">The core content display items.</param>
    /// <returns>An observable collection of ViewModel content display items.</returns>
    private ObservableCollection<ContentDisplayItem> ConvertToViewModelContentDisplayItems(
        IEnumerable<Core.Models.Content.ContentDisplayItem> coreItems)
        => new(coreItems.Select(ConvertToViewModelContentDisplayItem));
}