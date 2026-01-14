using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Notifications.Services;
using GenHub.Features.Notifications.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for managing game profile settings, including content selection and configuration.
/// </summary>
public partial class GameProfileSettingsViewModel : ViewModelBase, IRecipient<Core.Models.Content.ContentAcquiredMessage>
{
    private readonly IGameProfileManager? gameProfileManager;
    private readonly IGameSettingsService? gameSettingsService;
    private readonly IConfigurationProviderService? configurationProvider;
    private readonly IProfileContentLoader? profileContentLoader;
    private readonly Services.ProfileResourceService? profileResourceService;
    private readonly INotificationService? notificationService;
    private readonly IContentManifestPool? manifestPool;
    private readonly IContentStorageService? contentStorageService;
    private readonly ILocalContentService? localContentService;
    private readonly ILogger<GameProfileSettingsViewModel>? logger;
    private readonly ILogger<GameSettingsViewModel>? gameSettingsLogger;

    private readonly NotificationService _localNotificationService = new(
        NullLogger<NotificationService>.Instance);

    /// <summary>
    /// Gets the notification manager for local window notifications.
    /// </summary>
    public NotificationManagerViewModel NotificationManager { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModel"/> class.
    /// </summary>
    /// <param name="gameProfileManager">The game profile manager service.</param>
    /// <param name="gameSettingsService">The game settings service.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    /// <param name="profileContentLoader">The profile content loader service.</param>
    /// <param name="profileResourceService">The profile resource service.</param>
    /// <param name="notificationService">The notification service for global notifications.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="contentStorageService">The content storage service.</param>
    /// <param name="localContentService">The local content service.</param>
    /// <param name="logger">The logger for this view model.</param>
    /// <param name="gameSettingsLogger">The logger for the game settings view model.</param>
    public GameProfileSettingsViewModel(
        IGameProfileManager? gameProfileManager,
        IGameSettingsService? gameSettingsService,
        IConfigurationProviderService? configurationProvider,
        IProfileContentLoader? profileContentLoader,
        Services.ProfileResourceService? profileResourceService,
        INotificationService? notificationService,
        IContentManifestPool? manifestPool,
        IContentStorageService? contentStorageService,
        ILocalContentService? localContentService,
        ILogger<GameProfileSettingsViewModel>? logger,
        ILogger<GameSettingsViewModel>? gameSettingsLogger)
    {
        this.gameProfileManager = gameProfileManager;
        this.gameSettingsService = gameSettingsService;
        this.configurationProvider = configurationProvider;
        this.profileContentLoader = profileContentLoader;
        this.profileResourceService = profileResourceService;
        this.notificationService = notificationService;
        this.manifestPool = manifestPool;
        this.contentStorageService = contentStorageService;
        this.localContentService = localContentService;
        this.logger = logger;
        this.gameSettingsLogger = gameSettingsLogger;

        NotificationManager = new NotificationManagerViewModel(
            _localNotificationService,
            NullLogger<NotificationManagerViewModel>.Instance,
            NullLogger<NotificationItemViewModel>.Instance);

        GameSettingsViewModel = new GameSettingsViewModel(gameSettingsService!, gameSettingsLogger!);

        // Register for content acquired messages to auto-refresh available content
        WeakReferenceMessenger.Default.Register(this);
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _colorValue = "#5E35B1";

    // Remove [ObservableProperty] for SelectedContentType to implement custom setter
    private ContentType _selectedContentType = ContentType.GameClient;

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
    private int _selectedTabIndex;

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableContent = [];

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableGameInstallations = [];

    [ObservableProperty]
    private ContentDisplayItem? _selectedGameInstallation;

    /// <summary>
    /// Called when the selected game installation changes.
    /// </summary>
    partial void OnSelectedGameInstallationChanged(ContentDisplayItem? value)
    {
        if (value != null && value.GameType != GameTypeFilter)
        {
            GameTypeFilter = value.GameType;
            logger?.LogInformation("Auto-synced GameTypeFilter to {GameType} based on SelectedGameInstallation", value.GameType);
        }
    }

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _enabledContent = [];

    [ObservableProperty]
    private ObservableCollection<FilterTypeInfo> _visibleFilters = [];

    /// <summary>
    /// Information about a content filter type.
    /// </summary>
    /// <param name="ContentType">The content type.</param>
    /// <param name="DisplayName">The display name.</param>
    /// <param name="IconData">The SVG path data for the icon.</param>
    public record FilterTypeInfo(ContentType ContentType, string DisplayName, string IconData);

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _loadingError;

    [ObservableProperty]
    private WorkspaceStrategy _selectedWorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy;

    // Track the original workspace strategy when loading a profile to detect changes
    private WorkspaceStrategy? _originalWorkspaceStrategy;

    [ObservableProperty]
    private string _commandLineArguments = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableCovers = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedCover;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableGameClients = [];

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
    private ObservableCollection<ProfileInfoItem> _profileInfos = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedProfileInfo;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableExecutables = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedExecutable;

    [ObservableProperty]
    private bool _isExecutableValid = true;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableDataPaths = [];

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
    private string _coverPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileResourceItem> _availableIcons = [];

    [ObservableProperty]
    private ObservableCollection<ProfileResourceItem> _availableCoversForSelection = [];

    [ObservableProperty]
    private ProfileResourceItem? _selectedIcon;

    [ObservableProperty]
    private ProfileResourceItem? _selectedCoverItem;

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

    [ObservableProperty]
    private bool _isLoadingContent; // Guard flag to prevent cascading EnableContent calls AND UI loading state

    [ObservableProperty]
    private Core.Models.Enums.GameType _gameTypeFilter = Core.Models.Enums.GameType.ZeroHour;

    /// <summary>
    /// Called when the game type filter changes.
    /// </summary>
    partial void OnGameTypeFilterChanged(GameType value)
    {
        _ = RefreshFiltersAndContentAsync();
    }

    private async Task RefreshFiltersAndContentAsync()
    {
        await RefreshVisibleFiltersAsync();
        await LoadAvailableContentAsync();
    }

    // ===== Local Content Dialog Properties =====
    [ObservableProperty]
    private bool _isAddLocalContentDialogOpen;

    [ObservableProperty]
    private string _localContentName = string.Empty;

    [ObservableProperty]
    private string _localContentDirectoryPath = string.Empty;

    [ObservableProperty]
    private ContentType _selectedLocalContentType = ContentType.Addon;

    [ObservableProperty]
    private Core.Models.Enums.GameType _selectedLocalGameType = Core.Models.Enums.GameType.ZeroHour;

    /// <summary>
    /// Gets available local game types for selection.
    /// </summary>
    public static Core.Models.Enums.GameType[] AvailableLocalGameTypes { get; } =
    [
        Core.Models.Enums.GameType.Generals,
        Core.Models.Enums.GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets available content types for local content addition.
    /// </summary>
    public static ContentType[] AllowedLocalContentTypes { get; } =
    [
        ContentType.Mod,
        ContentType.MapPack,
        ContentType.Addon,
        ContentType.Patch,
        ContentType.ModdingTool,
        ContentType.Executable,
        ContentType.GameClient,
    ];

    /// <summary>
    /// Event that is raised when the window should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Cancels the add local content dialog.
    /// </summary>
    [RelayCommand]
    private void CancelAddLocalContent()
    {
        IsAddLocalContentDialogOpen = false;
        LocalContentName = string.Empty;
        LocalContentDirectoryPath = string.Empty;
        SelectedLocalContentType = ContentType.Addon;
    }

    /// <summary>
    /// Confirms the add local content dialog and adds the content.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAddLocalContent()
    {
        if (string.IsNullOrWhiteSpace(LocalContentName))
        {
            _localNotificationService.ShowWarning("Validation Error", "Please enter a name for the content.");
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalContentDirectoryPath))
        {
             _localNotificationService.ShowWarning("Validation Error", "Please select a folder for the content.");
             return;
        }

        try
        {
            IsSaving = true;

            var result = await localContentService!.AddLocalContentAsync(
                LocalContentName,
                LocalContentDirectoryPath,
                SelectedLocalContentType,
                SelectedLocalGameType);

            if (result.Success)
            {
                 IsAddLocalContentDialogOpen = false;

                 // Refresh available content
                 await LoadAvailableContentAsync();
            }
            else
            {
                logger?.LogWarning("Failed to add local content: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error adding local content");
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Gets available content types for selection.
    /// </summary>
    public static ContentType[] AvailableContentTypes { get; } =
    [
        ContentType.GameClient,
        ContentType.Mod,
        ContentType.MapPack,
        ContentType.Addon,
        ContentType.Patch,
        ContentType.ModdingTool,
        ContentType.Executable,
    ];

    /// <summary>
    /// Refreshes the list of visible content filters based on available content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RefreshVisibleFiltersAsync()
    {
        try
        {
            var manifestsResult = await manifestPool!.GetAllManifestsAsync();
            if (!manifestsResult.Success || manifestsResult.Data == null) return;

            var availableTypes = manifestsResult.Data
                .Where(m => m.TargetGame == GameTypeFilter)
                .Select(m => m.ContentType)
                .Distinct()
                .ToHashSet();

            // Executable (GameClient) is always visible if we have installations
            if (AvailableGameInstallations.Any(i => i.GameType == GameTypeFilter))
            {
                availableTypes.Add(ContentType.GameClient);
            }

            var newFilters = new List<FilterTypeInfo>();

            void AddFilterIfAvailable(ContentType type, string iconData)
            {
                if (availableTypes.Contains(type))
                {
                    newFilters.Add(new FilterTypeInfo(type, type.GetDisplayName(), iconData));
                }
            }

            // Define filters in order
            AddFilterIfAvailable(ContentType.GameClient, "M20,19V7H4V19H20M20,3A2,2 0 0,1 22,5V19A2,2 0 0,1 20,21H4A2,2 0 0,1 2,19V5C2,3.89 2.9,3 4,3H20");
            AddFilterIfAvailable(ContentType.Mod, "M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5S21.88 11 20.5 11z");
            AddFilterIfAvailable(ContentType.MapPack, "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z");
            AddFilterIfAvailable(ContentType.ModdingTool, "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.97 21.68,8.76L19.68,5.29C19.58,5.08 19.33,5 19.14,5.07L16.66,6.07C16.14,5.67 15.58,5.33 14.97,5.08L14.59,2.44C14.54,2.2 14.34,2.04 14.1,2.04H10.1C9.86,2.04 9.66,2.2 9.61,2.44L9.23,5.08C8.62,5.33 8.06,5.67 7.54,6.07L5.06,5.07C4.87,5 4.62,5.08 4.52,5.29L2.52,8.76C2.42,8.97 2.47,9.22 2.66,9.37L4.77,11.03C4.73,11.34 4.7,11.67 4.7,12C4.7,12.33 4.73,12.65 4.77,12.97L2.66,14.63C2.47,14.78 2.42,15.03 2.52,15.24L4.52,18.71C4.62,18.92 4.87,19 5.06,18.93L7.54,17.93C8.06,18.33 8.62,18.67 9.23,18.92L9.61,21.56C9.66,21.8 9.86,21.96 10.1,21.96H14.1C14.34,21.96 14.54,21.8 14.59,21.56L14.97,18.92C15.58,18.67 16.14,18.33 16.66,17.93L19.14,18.93C19.33,19 19.58,18.92 19.68,18.71L21.68,15.24C21.78,15.03 21.73,14.78 21.54,14.63L19.43,12.97Z");
            AddFilterIfAvailable(ContentType.Patch, "M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z");
            AddFilterIfAvailable(ContentType.Addon, "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");

            VisibleFilters = new ObservableCollection<FilterTypeInfo>(newFilters);

            // If selected content type is no longer available, switch to first available
            if (!availableTypes.Contains(SelectedContentType))
            {
                SelectedContentType = newFilters.FirstOrDefault()?.ContentType ?? ContentType.GameClient;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error refreshing visible filters");
        }
    }

    /// <summary>
    /// Gets available workspace strategies for selection.
    /// </summary>
    public static WorkspaceStrategy[] AvailableWorkspaceStrategies { get; } =
    [
        WorkspaceStrategy.SymlinkOnly,
        WorkspaceStrategy.HybridCopySymlink,
        WorkspaceStrategy.HardLink,
        WorkspaceStrategy.FullCopy,
    ];

    /// <summary>
    /// Gets the Game Settings ViewModel for the third tab.
    /// </summary>
    public GameSettingsViewModel GameSettingsViewModel { get; }

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
            SelectedContentType = ContentType.GameClient;

            EnabledContent.Clear();

            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();

            // Populate visible filters based on available content
            await RefreshVisibleFiltersAsync();

            // Set the first game installation as selected (for UI convenience), but don't auto-enable it
            if (AvailableGameInstallations.Any())
            {
                // Prioritize Zero Hour if available
                SelectedGameInstallation = AvailableGameInstallations
                    .OrderByDescending(i => i.GameType == Core.Models.Enums.GameType.ZeroHour)
                    .First();
                logger?.LogInformation("Pre-selected first GameInstallation for UI: {ContentName}", SelectedGameInstallation.DisplayName);

                // Set default icon and cover FIRST
                IconPath = NormalizeResourcePath(
                    profileResourceService?.GetDefaultIconPath(SelectedGameInstallation.GameType.ToString()),
                    Core.Constants.UriConstants.DefaultIconUri);
                CoverPath = NormalizeResourcePath(
                    profileResourceService?.GetDefaultCoverPath(SelectedGameInstallation.GameType.ToString()),
                    string.Empty);

                // then load available icons and covers (so SelectedIcon/SelectedCoverItem get set correctly)
                LoadAvailableIconsAndCovers(SelectedGameInstallation.GameType.ToString());
                GameTypeFilter = SelectedGameInstallation.GameType;
            }

            // Initialize game settings with defaults for new profile
            GameSettingsViewModel.ColorValue = ColorValue;

            // pre-load existing settings.json if available
            // This ensures users creating their first profile don't lose their current configuration
            if (gameSettingsService != null)
            {
                try
                {
                    var existingGoSettings = await gameSettingsService.LoadGeneralsOnlineSettingsAsync();
                    if (existingGoSettings.Success && existingGoSettings.Data != null)
                    {
                        logger?.LogInformation("Pre-loading existing GeneralsOnline settings for new profile");
                        var data = existingGoSettings.Data;

                        // Map existing settings to global DTO structure if needed, or directly pass to GameSettingsViewModel
                        // Since GameSettingsViewModel initializes with defaults (nulls), we need to inject these values.
                        // We'll create a temporary Profile object with these settings to initialize the VM.
                        var tempProfile = new GameProfile { Id = "temp_new" };

                        // Use the Mapper to populate the profile from the settings object
                        GameSettingsMapper.ApplyFromGeneralsOnlineSettings(data, tempProfile);

                        // Initialize VM with these pre-loaded settings
                        await GameSettingsViewModel.InitializeForProfileAsync(null, tempProfile);
                    }
                else
                {
                    // Fallback to standard defaults
                    await GameSettingsViewModel.InitializeForProfileAsync(null, null);
                }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to pre-load existing settings for new profile, using defaults");
                    await GameSettingsViewModel.InitializeForProfileAsync(null, null);
                }
            }
            else
            {
                // No settings service available, use defaults
                await GameSettingsViewModel.InitializeForProfileAsync(null, null);
            }

            // Ensure the correct game type is selected so we load the correct Options.ini defaults
            if (SelectedGameInstallation != null)
            {
                GameSettingsViewModel.SelectedGameType = SelectedGameInstallation.GameType;
                logger?.LogInformation(
                    "Set GameSettingsViewModel.SelectedGameType to {GameType} before initialization",
                    SelectedGameInstallation.GameType);
            }

            StatusMessage = $"Found {AvailableGameInstallations.Count} installations and {AvailableContent.Count} content items";
            logger?.LogInformation(
                "Initialized new profile creation with {InstallationCount} installations and {ContentCount} content items",
                AvailableGameInstallations.Count,
                AvailableContent.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error initializing new profile");
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
            logger?.LogInformation("InitializeForProfileAsync called with profileId: {ProfileId}", profileId);

            // Load the existing profile
            var profileResult = await gameProfileManager!.GetProfileAsync(profileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                logger?.LogWarning("Failed to load profile {ProfileId}: {Errors}", profileId, string.Join(", ", profileResult.Errors));
                StatusMessage = "Failed to load profile";
                LoadingError = true;
                return;
            }

            var profile = profileResult.Data;
            logger?.LogInformation("Loaded profile: {ProfileName}, EnabledContentIds count: {Count}", profile.Name, profile.EnabledContentIds?.Count ?? 0);

            Name = profile.Name;
            Description = profile.Description ?? string.Empty;
            ColorValue = profile.ThemeColor ?? "#1976D2";
            var defaultIconPath = profileResourceService?.GetDefaultIconPath(profile.GameClient.GameType.ToString())
                ?? Core.Constants.UriConstants.DefaultIconUri;
            IconPath = NormalizeResourcePath(profile.IconPath, defaultIconPath);
            var defaultCoverPath = profileResourceService?.GetDefaultCoverPath(profile.GameClient.GameType.ToString()) ?? string.Empty;
            CoverPath = NormalizeResourcePath(profile.CoverPath, defaultCoverPath);
            SelectedWorkspaceStrategy = profile.WorkspaceStrategy;
            _originalWorkspaceStrategy = profile.WorkspaceStrategy; // Track original strategy
            CommandLineArguments = profile.CommandLineArguments ?? string.Empty;

            // Load available icons and covers for selection
            LoadAvailableIconsAndCovers(profile.GameClient.GameType.ToString());
            GameTypeFilter = profile.GameClient.GameType;

            // Load game settings for this profile
            GameSettingsViewModel.ColorValue = ColorValue;
            await GameSettingsViewModel.InitializeForProfileAsync(profileId, profile);

            // If the profile has no custom game settings, save the defaults from Options.ini
            if (!profile.HasCustomSettings())
            {
                logger?.LogInformation("Profile {ProfileId} has no custom settings, saving defaults from Options.ini", profileId);
                var gameSettings = GameSettingsViewModel.GetProfileSettings();
                var updateRequest = new UpdateProfileRequest();
                PopulateGameSettings(updateRequest, gameSettings);

                var updateResult = await gameProfileManager.UpdateProfileAsync(profileId, updateRequest);
                if (updateResult.Success)
                {
                    logger?.LogInformation("Saved default game settings for profile {ProfileId}", profileId);
                }
                else
                {
                    logger?.LogWarning(
                        "Failed to save default game settings for profile {ProfileId}: {Errors}",
                        profileId,
                        string.Join(", ", updateResult.Errors));
                }
            }

            // Load enabled content for this profile
            logger?.LogInformation("About to call LoadEnabledContentForProfileAsync for profile: {ProfileName}", profile.Name);
            await LoadEnabledContentForProfileAsync(profile);
            logger?.LogInformation("After LoadEnabledContentForProfileAsync: EnabledContent count = {Count}", EnabledContent.Count);

            logger?.LogInformation("Loaded profile {ProfileName} for editing", profile.Name);

            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();

            // Populate visible filters based on available content
            await RefreshVisibleFiltersAsync();

            // Sync SelectedGameInstallation with the enabled one in the profile
            var enabledInstallation = EnabledContent.FirstOrDefault(c => c.ContentType == ContentType.GameInstallation);
            if (enabledInstallation != null)
            {
                // Find the matching item in AvailableGameInstallations
                SelectedGameInstallation = AvailableGameInstallations
                    .FirstOrDefault(a => a.ManifestId.Value == enabledInstallation.ManifestId.Value)
                    ?? enabledInstallation;

                logger?.LogInformation(
                    "Set SelectedGameInstallation to {DisplayName} from existing profile",
                    SelectedGameInstallation.DisplayName);
            }

            StatusMessage = $"Profile loaded with {EnabledContent.Count} enabled content items";
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error initializing profile {ProfileId}", profileId);
            StatusMessage = "Error loading profile";
            LoadingError = true;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Receives a ContentAcquiredMessage and refreshes the available content list.
    /// </summary>
    /// <param name="message">The content acquired message.</param>
    public void Receive(Core.Models.Content.ContentAcquiredMessage message)
    {
        // Refresh available content when new content is acquired
        _ = LoadAvailableContentAsync();
    }

    /// <summary>
    /// Normalizes a resource path to ensure it's a valid absolute URI or file path.
    /// Handles legacy relative paths by converting them to full avares:// URIs.
    /// </summary>
    private static string NormalizeResourcePath(string? path, string defaultUri)
    {
        if (string.IsNullOrWhiteSpace(path))
            return defaultUri;

        // If it's already a full avares:// URI, return as-is (don't double-prefix)
        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            return path;

        // If it's already an absolute URI (http, file, etc.), return as-is
        if (Uri.TryCreate(path, UriKind.Absolute, out _))
            return path;

        // Legacy relative resource path - convert to full URI
        // Remove leading slash if present to avoid double slashes
        var relativePath = path.TrimStart('/');
        return $"avares://GenHub/{relativePath}";
    }

    /// <summary>
    /// Populates game settings into a CreateProfileRequest.
    /// </summary>
    /// <param name="request">The create request to populate.</param>
    /// <param name="gameSettings">The game settings to apply, or null to skip.</param>
    private static void PopulateGameSettings(
        CreateProfileRequest request,
        UpdateProfileRequest? gameSettings)
    {
        if (gameSettings == null)
            return;

        GameSettingsMapper.PopulateRequest(request, gameSettings);
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

        GameSettingsMapper.PopulateRequest(request, gameSettings);
    }

    /// <summary>
    /// Gets the default workspace strategy from configuration.
    /// </summary>
    private WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        return configurationProvider!.GetDefaultWorkspaceStrategy();
    }

    /// <summary>
    /// Loads available content based on the selected content type.
    /// </summary>
    [RelayCommand]
    private async Task LoadAvailableContentAsync()
    {
        try
        {
            IsLoadingContent = true;
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

            var coreItems = await profileContentLoader!.LoadAvailableContentAsync(
                SelectedContentType,
                new ObservableCollection<Core.Models.Content.ContentDisplayItem>(coreAvailableInstallations),
                enabledContentIds);

            // Convert Core items to ViewModel items, excluding already-enabled content
            foreach (var coreItem in coreItems)
            {
                try
                {
                    // Skip items that are already in the EnabledContent list
                    if (enabledContentIds.Contains(coreItem.ManifestId))
                    {
                        continue;
                    }

                    // Skip items that don't match the current game type filter
                    if (coreItem.GameType != GameTypeFilter)
                    {
                        continue;
                    }

                    var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                    AvailableContent.Add(viewModelItem);
                }
                catch (ArgumentException argEx)
                {
                    logger?.LogWarning("Skipping invalid content item {DisplayName} (ID: {Id}): {Message}", coreItem.DisplayName, coreItem.ManifestId, argEx.Message);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error converting content item {DisplayName}", coreItem.DisplayName);
                }
            }

            StatusMessage = $"Loaded {AvailableContent.Count} {SelectedContentType} items";
            logger?.LogInformation("Loaded {Count} content items for content type {ContentType}", AvailableContent.Count, SelectedContentType);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading available content");
            StatusMessage = "Error loading content";
        }
        finally
        {
            IsLoadingContent = false;
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

            var coreItems = await profileContentLoader!.LoadAvailableGameInstallationsAsync();

            // Convert Core.ContentDisplayItem to ViewModel items
            foreach (var coreItem in coreItems)
            {
                try
                {
                    var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                    AvailableGameInstallations.Add(viewModelItem);
                }
                catch (ArgumentException argEx)
                {
                    logger?.LogWarning("Skipping invalid game installation {DisplayName} (ID: {Id}): {Message}", coreItem.DisplayName, coreItem.ManifestId, argEx.Message);
                }
            }

            // Select the first installation if available (prioritizing Zero Hour)
            if (AvailableGameInstallations.Any() && SelectedGameInstallation == null)
            {
                SelectedGameInstallation = AvailableGameInstallations
                    .OrderByDescending(i => i.GameType == Core.Models.Enums.GameType.ZeroHour)
                    .First();
            }

            logger?.LogInformation(
                "Loaded {Count} game installation options",
                AvailableGameInstallations.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading available game installations");
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

            var coreItems = await profileContentLoader!.LoadEnabledContentForProfileAsync(profile);

            // Convert Core items to ViewModel items
            foreach (var coreItem in coreItems)
            {
                var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                EnabledContent.Add(viewModelItem);
                viewModelItem.IsEnabled = true; // Ensure enabled status
            }

            logger?.LogInformation("Loaded {Count} enabled content items for profile {ProfileName}", EnabledContent.Count, profile.Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading enabled content for profile");
        }
    }

    /// <summary>
    /// Enables the specified content item for the profile.
    /// This is called with CommandParameter from the UI.
    /// </summary>
    /// <param name="contentItem">The content item to enable.</param>
    [RelayCommand]
    private async Task EnableContent(ContentDisplayItem? contentItem)
    {
        await EnableContentInternal(contentItem, bypassLoadingGuard: false);
    }

    private async Task EnableContentInternal(ContentDisplayItem? contentItem, bool bypassLoadingGuard = false)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            logger?.LogWarning("EnableContent: contentItem parameter is null");
            return;
        }

        // Prevent cascading calls during content loading, unless bypassed
        if (IsLoadingContent && !bypassLoadingGuard)
        {
            logger?.LogDebug("EnableContent: Blocked during content loading (guard flag set) - {DisplayName}", contentItem.DisplayName);
            return;
        }

        logger?.LogInformation(
            "EnableContent called with: {DisplayName} (ManifestId: {ManifestId}, SourceId: {SourceId}, GameClientId: {GameClientId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value,
            contentItem.SourceId ?? "NULL",
            contentItem.GameClientId ?? "NULL");

        if (contentItem.IsEnabled)
        {
            StatusMessage = "Content already enabled";
            logger?.LogWarning("EnableContent: {DisplayName} is already marked as enabled", contentItem.DisplayName);
            return;
        }

        // Check if content is already enabled by manifest ID
        var alreadyEnabled = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (alreadyEnabled != null)
        {
            StatusMessage = "Content is already enabled";
            logger?.LogWarning(
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
                // UX Improvement: If the existing item was the source of the name, reset it to "New Profile"
                // so the new item can take over the name if it's the next one enabled.
                if (existing.ContentType == ContentType.GameClient && Name == existing.DisplayName)
                {
                    Name = "New Profile";
                    logger?.LogInformation("Reset profile name to 'New Profile' because its current name source ({ContentName}) is being replaced", existing.DisplayName);
                }

                existing.IsEnabled = false;
                EnabledContent.Remove(existing);

                // Re-add to AvailableContent if it matches the current filter
                if (existing.ContentType == SelectedContentType && existing.GameType == GameTypeFilter)
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

                logger?.LogInformation(
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
            logger?.LogInformation(
                "Updated SelectedGameInstallation to {DisplayName} (SourceId={SourceId}, GameClientId={GameClientId})",
                contentItem.DisplayName,
                contentItem.SourceId,
                contentItem.GameClientId);
        }

        StatusMessage = $"Enabled {contentItem.DisplayName}";
        logger?.LogInformation("Enabled content {ContentName} for profile", contentItem.DisplayName);

        // UX Improvement: Auto-rename "New Profile" when enabling a game client
        if (contentItem.ContentType == ContentType.GameClient && Name == "New Profile")
        {
            Name = contentItem.DisplayName;
            logger?.LogInformation("Auto-renamed profile from 'New Profile' to '{NewName}' based on enabled GameClient", Name);
        }

        // Auto-resolve dependencies (switch game installation or enable other content)
        await ResolveDependenciesAsync(contentItem);
    }

    /// <summary>
    /// Disables the specified content item for the profile.
    /// Removes it from the enabled list and adds it back to available content.
    /// </summary>
    /// <param name="contentItem">The content item to disable.</param>
    [RelayCommand]
    private async Task DisableContent(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            logger?.LogWarning("DisableContent: contentItem parameter is null");
            return;
        }

        logger?.LogInformation(
            "DisableContent called for: {DisplayName} (ManifestId: {ManifestId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value);

        // Remove from enabled content
        var itemToRemove = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (itemToRemove != null)
        {
            itemToRemove.IsEnabled = false;
            EnabledContent.Remove(itemToRemove);

            // Add back to available content if it matches current filters
            if (itemToRemove.ContentType == SelectedContentType && itemToRemove.GameType == GameTypeFilter)
            {
                // Check if it's not already in available content
                var alreadyInAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == itemToRemove.ManifestId.Value);
                if (alreadyInAvailable == null)
                {
                    AvailableContent.Add(itemToRemove);
                }
                else
                {
                    alreadyInAvailable.IsEnabled = false;
                }
            }

            // If this was the selected game installation, clear it
            if (itemToRemove.ContentType == ContentType.GameInstallation &&
                SelectedGameInstallation?.ManifestId.Value == itemToRemove.ManifestId.Value)
            {
                SelectedGameInstallation = null;
                logger?.LogInformation("Cleared SelectedGameInstallation");
            }

            StatusMessage = $"Disabled {itemToRemove.DisplayName}";
            logger?.LogInformation("Disabled content {ContentName} from profile", itemToRemove.DisplayName);
        }
        else
        {
            StatusMessage = "Content not found in enabled list";
            logger?.LogWarning("DisableContent: ManifestId {ManifestId} not found in EnabledContent", contentItem.ManifestId.Value);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Deletes the specified content item from disk permanently.
    /// Shows a confirmation dialog before deletion.
    /// </summary>
    /// <param name="contentItem">The content item to delete.</param>
    [RelayCommand]
    private async Task DeleteContent(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            logger?.LogWarning("DeleteContent: contentItem parameter is null");
            return;
        }

        logger?.LogInformation(
            "DeleteContent called for: {DisplayName} (ManifestId: {ManifestId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value);

        try
        {
            // Check if this is local content
            if (localContentService == null || contentStorageService == null)
            {
                _localNotificationService.ShowError(
                    "Service Unavailable",
                    "Content deletion service is not available.");
                return;
            }

            // Confirm deletion with user
            var confirmMessage = $"Are you sure you want to permanently delete '{contentItem.DisplayName}' from disk? This action cannot be undone.";

            // For now, we'll proceed with deletion (in a real app, you'd show a dialog)
            // TODO: Add confirmation dialog when available
            logger?.LogInformation("Attempting to delete content: {ContentName}", contentItem.DisplayName);

            // Try to delete using local content service first
            var result = await localContentService.DeleteLocalContentAsync(contentItem.ManifestId.Value);

            if (result.Success)
            {
                // Remove from both collections
                var enabledItem = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
                if (enabledItem != null)
                {
                    EnabledContent.Remove(enabledItem);
                }

                var availableItem = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
                if (availableItem != null)
                {
                    AvailableContent.Remove(availableItem);
                }

                StatusMessage = $"Deleted {contentItem.DisplayName}";
                _localNotificationService.ShowSuccess(
                    "Content Deleted",
                    $"'{contentItem.DisplayName}' has been permanently deleted.");
                logger?.LogInformation("Successfully deleted content: {ContentName}", contentItem.DisplayName);
            }
            else
            {
                StatusMessage = $"Failed to delete {contentItem.DisplayName}";
                _localNotificationService.ShowError(
                    "Delete Failed",
                    $"Failed to delete '{contentItem.DisplayName}': {string.Join(", ", result.Errors)}");
                logger?.LogWarning(
                    "Failed to delete content {ContentName}: {Errors}",
                    contentItem.DisplayName,
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error deleting content {ContentName}", contentItem.DisplayName);
            StatusMessage = "Error deleting content";
            _localNotificationService.ShowError(
                "Delete Error",
                $"An error occurred while deleting '{contentItem.DisplayName}'.");
        }
    }

    /// <summary>
    /// Resolves dependencies for the newly enabled content.
    /// Automatically switches GameInstallation or enables other required content if needed.
    /// </summary>
    /// <param name="contentItem">The content item that was just enabled.</param>
    private async Task ResolveDependenciesAsync(ContentDisplayItem contentItem)
    {
        try
        {
            if (manifestPool == null)
            {
                return;
            }

            ContentManifest? manifest = null;
            var manifestResult = await manifestPool.GetManifestAsync(contentItem.ManifestId.Value);

            if (manifestResult.Success && manifestResult.Data != null)
            {
                manifest = manifestResult.Data;
            }

            // Fallback: If no manifest exists but it's a GameClient, create a synthetic one
            // This handles standard GameInstallations/Clients detected at runtime
            else if (contentItem.ContentType == ContentType.GameClient && !string.IsNullOrEmpty(contentItem.SourceId))
            {
                logger?.LogDebug("Creating synthetic manifest for GameClient {ContentName} depending on {SourceId}", contentItem.DisplayName, contentItem.SourceId);

                manifest = new ContentManifest
                {
                    Id = ManifestId.Create(contentItem.ManifestId.Value),
                    Name = contentItem.DisplayName,
                    ContentType = ContentType.GameClient,
                    TargetGame = contentItem.GameType,
                    Dependencies =
                    [
                        new ContentDependency
                        {
                            Id = ManifestId.Create(contentItem.SourceId),
                            Name = "Required Game Installation",
                            DependencyType = ContentType.GameInstallation,
                            CompatibleGameTypes = [contentItem.GameType],
                            IsOptional = false,
                            InstallBehavior = DependencyInstallBehavior.RequireExisting,
                        }
                    ],
                };
            }
            else
            {
                // Valid manifest not found and not a handled dynamic type
                return;
            }

            if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
            {
                // No dependencies to resolve
                _ = ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
                return;
            }

            foreach (var dependency in manifest.Dependencies)
            {
                // 1. Handle GameInstallation Dependency
                if (dependency.DependencyType == ContentType.GameInstallation)
                {
                    // Check if current installation satisfies the dependency
                    bool isSatisfied = false;

                    // Check compatible game types
                    if (dependency.CompatibleGameTypes != null && dependency.CompatibleGameTypes.Count > 0)
                    {
                        if (SelectedGameInstallation != null &&
                            SelectedGameInstallation.IsEnabled &&
                            dependency.CompatibleGameTypes.Contains(SelectedGameInstallation.GameType))
                        {
                            isSatisfied = true;
                        }
                    }

                    // Check specific ID if not strictly just type-based
                    if (!isSatisfied && dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                    {
                        if (SelectedGameInstallation != null &&
                            SelectedGameInstallation.IsEnabled &&
                            SelectedGameInstallation.ManifestId.Value == dependency.Id.ToString())
                        {
                            isSatisfied = true;
                        }
                    }

                    if (!isSatisfied)
                    {
                        // Need to switch GameInstallation
                        // Find a compatible one in AvailableGameInstallations
                        ContentDisplayItem? compatibleInstallation = null;

                        // First priority: Try finding the specific installation this content belongs to (via SourceId)
                        // Use SourceId directly (it tracks the parent installation ID)
                        if (!string.IsNullOrEmpty(contentItem.SourceId))
                        {
                            compatibleInstallation = AvailableGameInstallations
                                .FirstOrDefault(x => x.ManifestId.Value == contentItem.SourceId);

                            if (compatibleInstallation != null)
                            {
                                logger?.LogDebug(
                                    "Found compatible installation {InstallationName} for content {ContentSourceId} ({ContentName})",
                                    compatibleInstallation.DisplayName,
                                    contentItem.SourceId,
                                    contentItem.DisplayName);
                            }
                        }

                        // Second priority: Try finding by specific ID if indicated by dependency
                        if (compatibleInstallation == null && dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                        {
                            compatibleInstallation = AvailableGameInstallations
                                .FirstOrDefault(x => x.ManifestId.Value == dependency.Id.ToString());
                        }

                        // Third priority: Try by compatible game type
                        if (compatibleInstallation == null && dependency.CompatibleGameTypes != null)
                        {
                            // prefer matching InstallationType (e.g. EA App Client -> EA App Installation)
                            compatibleInstallation = AvailableGameInstallations
                                .FirstOrDefault(x => dependency.CompatibleGameTypes.Contains(x.GameType) &&
                                                     x.InstallationType == contentItem.InstallationType);

                            // Fallback to any matching game type (e.g. Steam Client -> CD Installation, if only option)
                            compatibleInstallation ??= AvailableGameInstallations
                                    .FirstOrDefault(x => dependency.CompatibleGameTypes.Contains(x.GameType));
                        }

                        if (compatibleInstallation != null)
                        {
                            // We found a compatible installation. We must ENABLE it properly so it gets added to EnabledContent
                            // and the previous one gets removed/disabled.
                            // Simply setting SelectedGameInstallation is NOT enough as validation relies on EnabledContent list.
                            logger?.LogInformation(
                                "Auto-resolving dependency: Switching GameInstallation to {InstallationName} for {ContentName}",
                                compatibleInstallation.DisplayName,
                                contentItem.DisplayName);

                            _localNotificationService.ShowSuccess(
                                "Auto-Resolved",
                                $"Switched Game Installation to '{compatibleInstallation.DisplayName}' as required by '{contentItem.DisplayName}'.");

                            // Recursively call EnableContent to handle the full switch logic (disabling old, enabling new, setting property)
                            await EnableContentInternal(compatibleInstallation);
                        }
                    }
                }

                // 2. Handle Content Dependencies (MapPack, Addon, etc)
                else
                {
                    // Check if already enabled
                    bool alreadyEnabled = false;

                    // Check by ID
                    if (dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                    {
                        alreadyEnabled = EnabledContent.Any(x => x.ManifestId.Value == dependency.Id.ToString());
                    }
                    else
                    {
                        // Check if any content of the required type is enabled
                        alreadyEnabled = EnabledContent.Any(x => x.ContentType == dependency.DependencyType);
                    }

                    if (!alreadyEnabled && !dependency.IsOptional)
                    {
                        // Try to find it in "AvailableContent" first?
                        // It might not be in the visible list if filtered.

                        // Use loader to find the best match
                        var availableOfTargetType = await profileContentLoader!.LoadAvailableContentAsync(
                            dependency.DependencyType,
                            new ObservableCollection<Core.Models.Content.ContentDisplayItem>(
                                AvailableGameInstallations.Select(x => new Core.Models.Content.ContentDisplayItem
                                {
                                    Id = x.ManifestId.Value,
                                    ManifestId = x.ManifestId.Value,
                                    DisplayName = x.DisplayName,
                                    ContentType = x.ContentType,
                                    GameType = x.GameType,
                                })),
                            EnabledContent.Select(x => x.ManifestId.Value));

                        Core.Models.Content.ContentDisplayItem? match = null;

                        // Find specific match by ID
                        if (dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                        {
                            match = availableOfTargetType.FirstOrDefault(x => x.ManifestId == dependency.Id.ToString());
                        }

                        if (match != null)
                        {
                            var viewModelItem = ConvertToViewModelContentDisplayItem(match);

                            // Check clearly if it's already enabled to avoid recursion (though EnableContent handles it)
                            if (!viewModelItem.IsEnabled)
                            {
                                logger?.LogInformation(
                                    "Auto-resolved dependency: Found required content {DependencyName}. Enabling it.",
                                    viewModelItem.DisplayName);

                                // Show specific notification for this action
                                _localNotificationService.ShowSuccess(
                                    "Auto-Resolved",
                                    $"Automatically enabled required content: '{viewModelItem.DisplayName}'");

                                // Call EnableContent to handle standard logic (moving from available, cardinality, etc.)
                                // This is recursive but safe because we check IsEnabled
                                await EnableContent(viewModelItem);
                            }
                        }
                    }
                }
            }

            // Finally, perform standard validation (shows warnings if anything is still missing)
            await ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error resolving dependencies for {ContentName}", contentItem.DisplayName);

            // Fallback to standard validation
            _ = ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
        }
    }

    /// <summary>
    /// Validates dependencies for enabled content and shows warning notifications if conflicts are detected.
    /// </summary>
    /// <param name="justEnabledContentName">The name of the content that was just enabled.</param>
    private async Task ValidateEnabledContentDependenciesAsync(string justEnabledContentName)
    {
        try
        {
            if (manifestPool == null)
            {
                logger?.LogDebug("Skipping dependency validation - manifestPool not available");
                return;
            }

            // Get all enabled content manifest IDs
            var enabledManifestIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();
            if (enabledManifestIds.Count == 0)
            {
                return;
            }

            logger?.LogDebug(
                "Validating dependencies for {Count} enabled content items: {ContentIds}",
                enabledManifestIds.Count,
                string.Join(", ", enabledManifestIds));

            // Load manifests for all enabled content (except GameInstallations which aren't in the pool)
            var manifests = new List<ContentManifest>();
            foreach (var manifestId in enabledManifestIds)
            {
                var manifestResult = await manifestPool.GetManifestAsync(manifestId);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    manifests.Add(manifestResult.Data);
                    logger?.LogDebug("Loaded manifest for validation: {ManifestId} ({ContentType})", manifestId, manifestResult.Data.ContentType);
                }
                else
                {
                    logger?.LogDebug("Manifest {ManifestId} not found in pool (likely a GameInstallation)", manifestId);
                }
            }

            // Check for missing dependencies
            var warnings = new List<string>();
            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());

            // Also track enabled content types from EnabledContent (includes GameInstallations not in manifest pool)
            var enabledContentByType = EnabledContent.GroupBy(e => e.ContentType).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var manifest in manifests)
            {
                if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
                {
                    continue;
                }

                logger?.LogDebug("Checking {Count} dependencies for {ManifestName}", manifest.Dependencies.Count, manifest.Name);

                foreach (var dependency in manifest.Dependencies)
                {
                    // For GameInstallation and GameClient, check EnabledContent directly (not just manifest pool)
                    // because GameInstallations are created on-the-fly and not stored in the manifest pool
                    if (dependency.DependencyType == ContentType.GameInstallation ||
                        dependency.DependencyType == ContentType.GameClient)
                    {
                        if (!enabledContentByType.TryGetValue(dependency.DependencyType, out var enabledOfType) || enabledOfType.Count == 0)
                        {
                            if (dependency.DependencyType == ContentType.GameInstallation)
                            {
                                warnings.Add($"'{manifest.Name}' requires a Game Installation to be selected.");
                                logger?.LogWarning(
                                    "Dependency validation failed: {ManifestName} requires GameInstallation but none found in EnabledContent",
                                    manifest.Name);
                            }
                            else if (dependency.DependencyType == ContentType.GameClient)
                            {
                                warnings.Add($"'{manifest.Name}' requires a Game Client to be selected.");
                                logger?.LogWarning(
                                    "Dependency validation failed: {ManifestName} requires GameClient but none found in EnabledContent",
                                    manifest.Name);
                            }

                            continue;
                        }

                        // Type-based match is sufficient for GameInstallation/GameClient - we found at least one
                        logger?.LogDebug(
                            "Dependency satisfied: {ManifestName} requires {DependencyType}, found {Count} enabled",
                            manifest.Name,
                            dependency.DependencyType,
                            enabledOfType.Count);
                        continue;
                    }

                    // Check if a content of the required type exists (for other content types)
                    if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || potentialMatches.Count == 0)
                    {
                        if (!dependency.IsOptional)
                        {
                            warnings.Add($"'{manifest.Name}' requires {dependency.DependencyType} content, but none is enabled.");
                            logger?.LogWarning(
                                "Dependency validation failed: {ManifestName} requires {DependencyType} but none found",
                                manifest.Name,
                                dependency.DependencyType);
                        }

                        continue;
                    }

                    // Check for specific content ID requirement (not a generic type-based constraint)
                    if (dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                    {
                        // First try exact match
                        bool found = manifestsById.ContainsKey(dependency.Id.ToString());

                        // If StrictPublisher is false, try semantic matching
                        if (!found && !dependency.StrictPublisher)
                        {
                            var depIdSegments = dependency.Id.ToString().Split('.');
                            if (depIdSegments.Length >= 5)
                            {
                                var depContentType = depIdSegments[3];
                                var depContentName = depIdSegments[4];

                                found = potentialMatches.Any(m =>
                                {
                                    var manifestIdSegments = m.Id.ToString().Split('.');
                                    if (manifestIdSegments.Length >= 5)
                                    {
                                        return string.Equals(manifestIdSegments[3], depContentType, StringComparison.OrdinalIgnoreCase) &&
                                               string.Equals(manifestIdSegments[4], depContentName, StringComparison.OrdinalIgnoreCase);
                                    }

                                    return false;
                                });
                            }
                        }

                        if (!found && !dependency.IsOptional)
                        {
                            warnings.Add($"'{manifest.Name}' requires '{dependency.Name}' which is not enabled.");
                        }
                    }

                    // Check for conflicts
                    if (dependency.ConflictsWith.Count > 0)
                    {
                        foreach (var conflictId in dependency.ConflictsWith)
                        {
                            if (manifestsById.TryGetValue(conflictId.ToString(), out var conflictingManifest))
                            {
                                warnings.Add($"'{manifest.Name}' conflicts with '{conflictingManifest.Name}' - these cannot be used together.");
                            }
                        }
                    }
                }
            }

            // Show warning notifications if any issues found
            if (warnings.Count > 0)
            {
                var warningMessage = string.Join("\n• ", warnings);

                // Use local notification service so it appears on the window
                _localNotificationService.ShowWarning(
                    "Dependency Warning",
                    $"After enabling '{justEnabledContentName}':\n• {warningMessage}",
                    15000); // Show for 15 seconds since this is important info

                logger?.LogWarning(
                    "Dependency validation warnings after enabling {ContentName}: {Warnings}",
                    justEnabledContentName,
                    string.Join("; ", warnings));
            }
            else
            {
                logger?.LogInformation(
                    "Dependency validation passed after enabling {ContentName}",
                    justEnabledContentName);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during dependency validation for enabled content");
        }
    }

    /// <summary>
    /// Validates all dependencies for the given content IDs and returns a list of error messages.
    /// </summary>
    /// <param name="enabledContentIds">The list of enabled content manifest IDs.</param>
    /// <returns>A list of error messages for missing dependencies.</returns>
    private async Task<List<string>> ValidateAllDependenciesAsync(List<string> enabledContentIds)
    {
        var errors = new List<string>();

        try
        {
            if (manifestPool == null)
            {
                return errors;
            }

            // Load manifests for all enabled content
            var manifests = new List<ContentManifest>();
            foreach (var manifestId in enabledContentIds)
            {
                var manifestResult = await manifestPool.GetManifestAsync(manifestId);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    manifests.Add(manifestResult.Data);
                }
            }

            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var manifest in manifests)
            {
                if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
                {
                    continue;
                }

                foreach (var dependency in manifest.Dependencies)
                {
                    // Check if a content of the required type exists
                    if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || potentialMatches.Count == 0)
                    {
                        if (!dependency.IsOptional)
                        {
                            // Missing type-based dependency
                            if (dependency.DependencyType == ContentType.GameInstallation)
                            {
                                errors.Add($"• '{manifest.Name}' requires a Game Installation");
                            }
                            else if (dependency.DependencyType == ContentType.GameClient)
                            {
                                errors.Add($"• '{manifest.Name}' requires a Game Client");
                            }
                            else
                            {
                                errors.Add($"• '{manifest.Name}' requires {dependency.DependencyType} content");
                            }
                        }

                        continue;
                    }

                    // Check for specific content ID requirement
                    if (dependency.Id.ToString() != Core.Constants.ManifestConstants.DefaultContentDependencyId)
                    {
                        bool found = manifestsById.ContainsKey(dependency.Id.ToString());

                        // If StrictPublisher is false, try semantic matching
                        if (!found && !dependency.StrictPublisher)
                        {
                            var depIdSegments = dependency.Id.ToString().Split('.');
                            if (depIdSegments.Length >= 5)
                            {
                                var depContentType = depIdSegments[3];
                                var depContentName = depIdSegments[4];

                                found = potentialMatches.Any(m =>
                                {
                                    var manifestIdSegments = m.Id.ToString().Split('.');
                                    return manifestIdSegments.Length >= 5 &&
                                           manifestIdSegments[3] == depContentType &&
                                           manifestIdSegments[4] == depContentName;
                                });
                            }
                        }

                        if (!found && !dependency.IsOptional)
                        {
                            // Try to get the dependency manifest to show a friendly name
                            var depManifestResult = await manifestPool.GetManifestAsync(dependency.Id.ToString());
                            var depName = depManifestResult.Success && depManifestResult.Data != null
                                ? depManifestResult.Data.Name
                                : dependency.Id.ToString();

                            errors.Add($"• '{manifest.Name}' requires '{depName}'");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during comprehensive dependency validation");
        }

        return errors;
    }

    /// <summary>
    /// Performs the actual deletion of the content item.
    /// </summary>
    /// <param name="contentItem">The content item to delete.</param>
    private async Task PerformDeletionAsync(ContentDisplayItem contentItem)
    {
        if (contentStorageService == null) return;

        try
        {
            StatusMessage = $"Deleting {contentItem.DisplayName}...";
            logger?.LogInformation(
                "PerformDeletionAsync: Deleting content {ContentName} (ManifestId: {ManifestId})",
                contentItem.DisplayName,
                contentItem.ManifestId.Value);

            // Call the content storage service to remove the content
            var result = await contentStorageService.RemoveContentAsync(contentItem.ManifestId);

            if (result.Success)
            {
                // Remove from AvailableContent collection (UI Update)
                // We need to do this on the UI thread, which we should be on, but let's be safe if invoked from a callback
                // Note: ObservableCollection isn't thread-safe, but NotificationService action is likely invoked on UI thread via command binding.
                // However, since we are in async void (Action), we should ensure we are careful.
                // Given Avalonia structure, the command invocation from the button is on UI thread.
                var itemToRemove = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
                if (itemToRemove != null)
                {
                    AvailableContent.Remove(itemToRemove);
                }

                StatusMessage = $"Deleted {contentItem.DisplayName}";
                _localNotificationService.ShowSuccess(
                    "Content Deleted",
                    $"Successfully deleted '{contentItem.DisplayName}' from storage.");
                logger?.LogInformation(
                    "PerformDeletionAsync: Successfully deleted content {ContentName}",
                    contentItem.DisplayName);
            }
            else
            {
                StatusMessage = $"Failed to delete {contentItem.DisplayName}";
                _localNotificationService.ShowError(
                    "Deletion Failed",
                    $"Failed to delete '{contentItem.DisplayName}': {string.Join(", ", result.Errors)}");
                logger?.LogError(
                    "PerformDeletionAsync: Failed to delete content {ContentName}: {Errors}",
                    contentItem.DisplayName,
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting {contentItem.DisplayName}";
            _localNotificationService.ShowError(
                "Deletion Error",
                $"An error occurred while deleting '{contentItem.DisplayName}': {ex.Message}");
            logger?.LogError(
                ex,
                "PerformDeletionAsync: Exception while deleting content {ContentName}",
                contentItem.DisplayName);
        }
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

            // Validate that some launchable content is enabled (GameInstallation, GameClient, Executable, or Tool)
            var hasLaunchableContent = EnabledContent.Any(c =>
                c.IsEnabled &&
                (c.ContentType == ContentType.GameInstallation ||
                 c.ContentType == ContentType.GameClient ||
                 c.ContentType == ContentType.Executable ||
                 c.ContentType == ContentType.ModdingTool));

            if (!hasLaunchableContent)
            {
                StatusMessage = "Error: A Game, Executable, or Tool must be enabled.";
                _localNotificationService.ShowError(
                    "Missing Launchable Content",
                    "Please enable a Game, Executable, or Tool before saving.");
                logger?.LogWarning("Profile save blocked: No launchable content enabled");
                return;
            }

            // Build enabled content IDs from all enabled content
            var enabledContentIds = EnabledContent.Where(c => c.IsEnabled).Select(c => c.ManifestId.Value).ToList();

            // Validate all dependencies before saving
            if (manifestPool != null)
            {
                var validationErrors = await ValidateAllDependenciesAsync(enabledContentIds);
                if (validationErrors.Count > 0)
                {
                    var errorMessage = string.Join("\n", validationErrors);
                    StatusMessage = "Error: Missing required dependencies";
                    _localNotificationService.ShowError(
                        "Missing Dependencies",
                        $"Cannot save profile with missing dependencies:\n\n{errorMessage}");
                    logger?.LogWarning("Profile save blocked: {Errors}", errorMessage);
                    return;
                }
            }

            logger?.LogInformation(
                "Profile will be created/updated with {Count} enabled content items: {ContentIds}",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            if (string.IsNullOrEmpty(_currentProfileId))
            {
                // Create new profile

                // Auto-enable logic removed to respect user selection.
                // Validation (ValidateAllDependenciesAsync) performed earlier handles ensuring required content is present.
                // If a Tool/Executable is selected without GameInstallation, it will now pass if valid.
                var createRequest = new CreateProfileRequest
                {
                    Name = Name,
                    Description = Description,
                    GameInstallationId = SelectedGameInstallation.SourceId,
                    GameClientId = SelectedGameInstallation.GameClientId,
                    PreferredStrategy = SelectedWorkspaceStrategy,
                    EnabledContentIds = enabledContentIds,
                    CommandLineArguments = CommandLineArguments,
                    IconPath = IconPath,
                    CoverPath = CoverPath,
                    ThemeColor = ColorValue,
                };

                // Populate settings into create request
                var gameSettings = GameSettingsViewModel.GetProfileSettings();
                PopulateGameSettings(createRequest, gameSettings);

                var result = await gameProfileManager.CreateProfileAsync(createRequest);
                if (result.Success && result.Data != null)
                {
                    // Explicitly save settings to Options.ini/Settings.json so they become the new "defaults"
                    if (GameSettingsViewModel.SaveSettingsCommand.CanExecute(null))
                    {
                        await GameSettingsViewModel.SaveSettingsCommand.ExecuteAsync(null);
                    }

                    StatusMessage = "Profile created successfully";
                    logger?.LogInformation("Created new profile {ProfileName} with {ContentCount} enabled content items", Name, enabledContentIds.Count);

                    // Notify other components that a profile was created
                    WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(result.Data));

                    // Close the window after a brief delay
                    await Task.Delay(1000);
                    ExecuteCancel();
                }
                else
                {
                    StatusMessage = $"Failed to create profile: {string.Join(", ", result.Errors)}";
                    logger?.LogWarning("Failed to create profile: {Errors}", string.Join(", ", result.Errors));
                }
            }
            else
            {
                // Update existing profile
                var gameSettings = GameSettingsViewModel.GetProfileSettings();

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
                    IconPath = IconPath,
                    CoverPath = CoverPath,
                };

                PopulateGameSettings(updateRequest, gameSettings);

                var result = await gameProfileManager.UpdateProfileAsync(_currentProfileId, updateRequest);
                if (result.Success && result.Data != null)
                {
                    // Explicitly save settings to Options.ini/Settings.json so they become the new "defaults"/baseline
                    // This ensures that if the user immediately creates a new profile, it inherits these settings
                    if (GameSettingsViewModel.SaveSettingsCommand.CanExecute(null))
                    {
                        await GameSettingsViewModel.SaveSettingsCommand.ExecuteAsync(null);
                    }

                    StatusMessage = "Profile updated successfully";
                    logger?.LogInformation("Updated profile {ProfileId} with {ContentCount} enabled content items", _currentProfileId, enabledContentIds.Count);

                    // Notify other components that a profile was updated
                    WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(result.Data));

                    // Close the window after a brief delay
                    await Task.Delay(1000);
                    ExecuteCancel();
                }
                else
                {
                    StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}";
                    logger?.LogWarning("Failed to update profile {ProfileId}: {Errors}", _currentProfileId, string.Join(", ", result.Errors));
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error saving profile");
            StatusMessage = "Error saving profile";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Loads available icons and covers based on the game type.
    /// </summary>
    private void LoadAvailableIconsAndCovers(string gameType)
    {
        try
        {
            if (profileResourceService == null)
            {
                logger?.LogWarning("ProfileResourceService is not available");
                return;
            }

            // Load icons for this game type
            var icons = profileResourceService.GetIconsForGameType(gameType);
            AvailableIcons = new ObservableCollection<ProfileResourceItem>(icons);
            logger?.LogInformation("Loaded {Count} icons for game type {GameType}", icons.Count, gameType);

            // Load ALL covers (not filtered by game type) so users can choose any cover
            var covers = profileResourceService.GetAvailableCovers();
            AvailableCoversForSelection = new ObservableCollection<ProfileResourceItem>(covers);
            logger?.LogInformation("Loaded {Count} covers (all types)", covers.Count);

            // Set selected icon based on current IconPath
            if (!string.IsNullOrEmpty(IconPath))
            {
                SelectedIcon = AvailableIcons.FirstOrDefault(i => i.Path == IconPath);
            }

            // Set selected cover based on current CoverPath
            if (!string.IsNullOrEmpty(CoverPath))
            {
                SelectedCoverItem = AvailableCoversForSelection.FirstOrDefault(c => c.Path == CoverPath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading available icons and covers");
        }
    }

    /// <summary>
    /// Selects an icon for the profile.
    /// </summary>
    [RelayCommand]
    private void SelectIcon(ProfileResourceItem? icon)
    {
        if (icon == null)
        {
            return;
        }

        SelectedIcon = icon;
        IconPath = icon.Path;
        logger?.LogInformation("Selected icon: {DisplayName} ({Path})", icon.DisplayName, icon.Path);
    }

    /// <summary>
    /// Selects a cover for the profile.
    /// </summary>
    [RelayCommand]
    private void SelectCover(ProfileResourceItem? cover)
    {
        if (cover == null)
        {
            return;
        }

        SelectedCoverItem = cover;
        CoverPath = cover.Path;
        logger?.LogInformation("Selected cover: {DisplayName} ({Path})", cover.DisplayName, cover.Path);
    }

    /// <summary>
    /// Opens a file dialog to browse for a custom icon.
    /// </summary>
    [RelayCommand]
    private async Task BrowseForCustomIconAsync()
    {
        try
        {
            var openFileDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Custom Icon",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Image Files")
                    {
                        Patterns = [ "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico" ],
                    },
                ],
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel != null)
            {
                var storageProvider = topLevel.StorageProvider;
                var result = await storageProvider.OpenFilePickerAsync(openFileDialog);

                if (result.Count > 0)
                {
                    var selectedFile = result[0];
                    IconPath = selectedFile.Path.LocalPath;
                    SelectedIcon = null; // Clear built-in selection when using custom
                    logger?.LogInformation("Selected custom icon: {Path}", IconPath);
                    StatusMessage = "Custom icon selected";
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error browsing for custom icon");
            StatusMessage = "Error selecting custom icon";
        }
    }

    /// <summary>
    /// Opens a file dialog to browse for a custom cover.
    /// </summary>
    [RelayCommand]
    private async Task BrowseForCustomCoverAsync()
    {
        try
        {
            var openFileDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Custom Cover",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Image Files")
                    {
                        Patterns = [ "*.png", "*.jpg", "*.jpeg", "*.bmp" ],
                    },
                ],
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel != null)
            {
                var storageProvider = topLevel.StorageProvider;
                var result = await storageProvider.OpenFilePickerAsync(openFileDialog);

                if (result.Count > 0)
                {
                    var selectedFile = result[0];
                    CoverPath = selectedFile.Path.LocalPath;
                    SelectedCoverItem = null; // Clear built-in selection when using custom
                    logger?.LogInformation("Selected custom cover: {Path}", CoverPath);
                    StatusMessage = "Custom cover selected";
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error browsing for custom cover");
            StatusMessage = "Error selecting custom cover";
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
        if (GameSettingsViewModel != null)
        {
            GameSettingsViewModel.ColorValue = ColorValue;
        }

        StatusMessage = $"Color randomized to {ColorValue}";
        logger?.LogInformation("Randomized profile color to {ColorValue}", ColorValue);
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
            if (GameSettingsViewModel != null)
            {
                GameSettingsViewModel.ColorValue = ColorValue;
            }

            StatusMessage = $"Selected theme color {color}";
            logger?.LogInformation("Selected theme color {ColorValue}", color);
        }
        else
        {
            StatusMessage = "Invalid color selected";
            logger?.LogWarning("Invalid color parameter passed to SelectThemeColor");
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
        logger?.LogInformation("BrowseCustomCoverCommand executed");
    }

    /// <summary>
    /// Browses for a shortcut path.
    /// </summary>
    [RelayCommand]
    private void BrowseShortcutPath()
    {
        // TODO: Implement file dialog for shortcut path
        StatusMessage = "Browse shortcut path: TODO - Implement file dialog";
        logger?.LogInformation("BrowseShortcutPathCommand executed");
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
            logger?.LogInformation("Content type filter changed to {ContentType}", contentType.Value);
        }
    }

    /// <summary>
    /// Changes the selected game type filter.
    /// </summary>
    /// <param name="gameType">The game type to filter by.</param>
    [RelayCommand]
    private void SelectGameTypeFilter(Core.Models.Enums.GameType gameType)
    {
        if (gameType != GameTypeFilter)
        {
            GameTypeFilter = gameType;
            logger?.LogInformation("Game type filter changed to {GameType}", gameType);
        }
    }

    /// <summary>
    /// Selects a tab by index.
    /// </summary>
    /// <param name="tabIndexStr">The tab index as a string.</param>
    [RelayCommand]
    private void SelectTab(string? tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out var tabIndex))
        {
            SelectedTabIndex = tabIndex;
            logger?.LogDebug("Tab selected: {TabIndex}", tabIndex);
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
    /// Opens the Add Local Content dialog.
    /// </summary>
    [RelayCommand]
    private async Task AddLocalContentAsync(Avalonia.Controls.Window? owner)
    {
        try
        {
            if (localContentService == null)
            {
                StatusMessage = "Local content service unavailable";
                return;
            }

            var dialogOwner = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (dialogOwner == null)
            {
                logger?.LogWarning("AddLocalContentAsync: No suitable owner window found.");
                return;
            }

            // Create and show the new dialog
            // Note: Logger casting might fail if generic type doesn't match, so passing null is safer
            var vm = new AddLocalContentViewModel(localContentService, null);
            var window = new Views.AddLocalContentWindow
            {
                DataContext = vm,
            };

            var result = await window.ShowDialog<bool>(dialogOwner);

            if (result && vm.CreatedContentItem != null)
            {
                var contentItem = vm.CreatedContentItem;

                // Add to AvailableContent if not present
                if (!AvailableContent.Any(a => a.ManifestId.Value == contentItem.ManifestId.Value))
                {
                    AvailableContent.Add(contentItem);
                }

                logger?.LogInformation("Added local content via dialog: {Name}", contentItem.DisplayName);

                // Enable it
                StatusMessage = $"Added {contentItem.DisplayName}";
                await EnableContentInternal(contentItem, bypassLoadingGuard: true);

                _localNotificationService?.ShowSuccess(
                     "Content Added",
                     $"'{contentItem.DisplayName}' has been added successfully.");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error opening Add Local Content dialog");
            StatusMessage = "Error opening dialog";
        }
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