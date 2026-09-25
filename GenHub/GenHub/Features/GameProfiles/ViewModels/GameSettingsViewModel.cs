using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the Game Settings tab in Profile Settings.
/// Manages Options.ini for Generals and Zero Hour.
/// </summary>
public partial class GameSettingsViewModel(
    IGameSettingsService gameSettingsService,
    ILogger<GameSettingsViewModel> logger,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null) : ViewModelBase
{
    /// <summary>
    /// Gets the available texture quality levels.
    /// </summary>
    public static IReadOnlyList<TextureQuality> TextureQualityValues { get; } = Enum.GetValues<TextureQuality>();

    private const TextureQuality MaxTextureQuality = TextureQuality.VeryHigh; // Will be VeryHigh when SH version supports 'very high' texture quality (see TheSuperHackers/GeneralsGameCode#1629)
    private const int TextureReductionOffset = GameSettingsConstants.TextureQuality.ReductionOffset;

    // Resolution validation constants
    private const int MinResolutionWidth = GameSettingsConstants.Resolution.MinWidth;
    private const int MaxResolutionWidth = GameSettingsConstants.Resolution.MaxWidth; // Supports up to 8K resolution; can be adjusted for larger displays in the future
    private const int MinResolutionHeight = GameSettingsConstants.Resolution.MinHeight;
    private const int MaxResolutionHeight = GameSettingsConstants.Resolution.MaxHeight;

    // Volume validation constants
    private const int MinVolume = GameSettingsConstants.Volume.Min;
    private const int MaxVolume = GameSettingsConstants.Volume.Max;

    // NumSounds validation constants
    private const int MinNumSounds = GameSettingsConstants.Audio.MinNumSounds;
    private const int MaxNumSounds = GameSettingsConstants.Audio.MaxNumSounds;

    // Notification and localization constants
    private const string WarningTitleKey = "GameProfiles.Settings.Notification.WarningTitle";
    private const string DefaultWarningTitle = "Settings Warning";
    private const string ServiceUnavailableMessageKey = "GameProfiles.Settings.Notification.ServiceUnavailableMessage";
    private const string DefaultServiceUnavailableMessage = "Game settings service not available";
    private const string UnknownGameTypeMessageKey = "GameProfiles.Settings.Notification.UnknownGameTypeMessage";
    private const string DefaultUnknownGameTypeMessage = "Cannot load settings for unknown game type";
    private const string ErrorLoadingSettingsTitleKey = "GameProfiles.Settings.Notification.ErrorLoadingSettingsTitle";
    private const string DefaultErrorLoadingSettingsTitle = "Error Loading Settings";
    private const string ErrorSavingSettingsTitleKey = "GameProfiles.Settings.Notification.ErrorSavingSettingsTitle";
    private const string DefaultErrorSavingSettingsTitle = "Error Saving Settings";
    private const string ErrorOpeningLocationTitleKey = "GameProfiles.Settings.Notification.ErrorOpeningLocationTitle";
    private const string DefaultErrorOpeningLocationTitle = "Error Opening Location";
    private const string ErrorOpeningLocationMessageKey = "GameProfiles.Settings.Notification.ErrorOpeningLocationMessage";
    private const string DefaultErrorOpeningLocationMessage = "Failed to open file location";
    private const string SettingsSavedTitleKey = "GameProfiles.Settings.Notification.SettingsSavedTitle";
    private const string DefaultSettingsSavedTitle = "Settings Saved";
    private const string SettingsSavedMessageKey = "GameProfiles.Settings.Notification.SettingsSavedMessage";
    private const string DefaultSettingsSavedMessage = "{0} settings saved successfully";
    private const string DefaultSettingsLoadedTitleKey = "GameProfiles.Settings.Notification.DefaultSettingsLoadedTitle";
    private const string DefaultDefaultSettingsLoadedTitle = "Default Settings Loaded";
    private const string DefaultSettingsLoadedMessageKey = "GameProfiles.Settings.Notification.DefaultSettingsLoadedMessage";
    private const string DefaultDefaultSettingsLoadedMessage = "Loaded default settings from Options.ini. Save the profile to persist these settings.";
    private const string ResolutionSetTitleKey = "GameProfiles.Settings.Notification.ResolutionSetTitle";
    private const string DefaultResolutionSetTitle = "Resolution Updated";
    private const string ResolutionSetMessageKey = "GameProfiles.Settings.Notification.ResolutionSetMessage";
    private const string DefaultResolutionSetMessage = "Resolution set to {0}x{1}";
    private const string InvalidResolutionPresetMessageKey = "GameProfiles.Settings.Notification.InvalidResolutionPresetMessage";
    private const string DefaultInvalidResolutionPresetMessage = "Invalid resolution preset: {0}";

    private static bool ParseBool(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value == "1";

    private static string BoolToString(bool value) => value ? "yes" : "no";

    private static bool TryParseResolution(string? preset, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(preset)) return false;

        var parts = preset.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], out width) || width < MinResolutionWidth || width > MaxResolutionWidth)
            return false;

        if (!int.TryParse(parts[1], out height) || height < MinResolutionHeight || height > MaxResolutionHeight)
            return false;

        return true;
    }

    private static string? GetAnySettingValue(IniOptions options, string key)
    {
        if (options.Video.AdditionalProperties.TryGetValue(key, out var vVal))
        {
            return vVal;
        }

        var tshKvp = options.AdditionalSections.FirstOrDefault(s =>
            string.Equals(s.Key, GameSettingsTheSuperHackersConstants.SectionName, StringComparison.OrdinalIgnoreCase));
        if (tshKvp.Value?.TryGetCaseInsensitive(key, out var tshVal) == true)
        {
            return tshVal;
        }

        if (options.Network.AdditionalProperties.TryGetValue(key, out var nVal))
        {
            return nVal;
        }

        return null;
    }

    private static string? GetFirstSettingValue(Dictionary<string, string>? primary, Dictionary<string, string>? fallback, string key)
    {
        if (primary?.TryGetCaseInsensitive(key, out var primaryVal) == true)
            return primaryVal;
        if (fallback?.TryGetCaseInsensitive(key, out var fallbackVal) == true)
            return fallbackVal;
        return null;
    }

    /// <summary>
    /// Gets or sets the action triggered when the view needs to scroll to a specific section.
    /// </summary>
    public Action<string>? ScrollToSectionRequested { get; set; }

    [RelayCommand]
    private void ScrollToSection(string sectionName)
    {
        ScrollToSectionRequested?.Invoke(sectionName);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZeroHour))]
    private GameType _selectedGameType;

    /// <summary>
    /// Gets a value indicating whether the selected game is Zero Hour.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable property SelectedGameType in partial view model")]
    public bool IsZeroHour => SelectedGameType == GameType.ZeroHour;

    [ObservableProperty]
    private bool _isTheSuperHackersVisible = true;

    [ObservableProperty]
    private bool _isGeneralsOnlineVisible = true;

    /// <summary>
    /// Gets a value indicating whether custom camera settings should be shown (for non-GeneralsOnline profiles).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable property IsGeneralsOnlineVisible in partial view model")]
    public bool IsCustomCameraVisible => !IsGeneralsOnlineVisible;

    /// <summary>
    /// Updates the visibility of client-specific setting categories based on active client flags.
    /// </summary>
    /// <param name="isTheSuperHackers">Whether TheSuperHackers client is enabled.</param>
    /// <param name="isGeneralsOnline">Whether GeneralsOnline client is enabled.</param>
    public void UpdateApplicableClientVisibility(bool isTheSuperHackers, bool isGeneralsOnline)
    {
        IsTheSuperHackersVisible = isTheSuperHackers;
        IsGeneralsOnlineVisible = isGeneralsOnline;
        OnPropertyChanged(nameof(IsCustomCameraVisible));

        if (isGeneralsOnline)
        {
            ResetCameraDefaults();
        }

        if (!IsTheSuperHackersVisible && SelectedCategory == SettingsCategory.TheSuperHackers)
        {
            SelectedCategory = SettingsCategory.Video;
        }

        if (!IsGeneralsOnlineVisible && SelectedCategory == SettingsCategory.GeneralsOnline)
        {
            SelectedCategory = SettingsCategory.Video;
        }
    }

    private SettingsCategory _selectedCategory = SettingsCategory.Video;

    /// <summary>
    /// Gets or sets the currently selected category in the sidebar.
    /// </summary>
    public SettingsCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                // Trigger scroll only if explicitly set (e.g. via UI click),
                // but we need to distinguish between "User Clicked" and "Scroll Spy Updated".
                // For now, the View will handle the distinction or we use a separate method for ScrollSpy updates.
                ScrollToSectionRequested?.Invoke(value.ToString() + "Section");
            }
        }
    }

    /// <summary>
    /// Updates the selected category from the scroll spy without triggering a scroll request.
    /// </summary>
    /// <param name="category">The new active category.</param>
    public void UpdateCategoryFromScroll(SettingsCategory category)
    {
        SetProperty(ref _selectedCategory, category, nameof(SelectedCategory));
    }

    [RelayCommand]
    private void SelectCategory(SettingsCategory category)
    {
        SelectedCategory = category;
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _optionsFileExists;

    [ObservableProperty]
    private string _optionsFilePath = string.Empty;

    // Audio Settings
    [ObservableProperty]
    private int _soundVolume = 70;

    [ObservableProperty]
    private int _threeDSoundVolume = 70;

    [ObservableProperty]
    private int _speechVolume = 70;

    [ObservableProperty]
    private int _musicVolume = 70;

    [ObservableProperty]
    private bool _audioEnabled = true;

    [ObservableProperty]
    private int _numSounds = 16;

    // Video Settings
    [ObservableProperty]
    private int _resolutionWidth = 800;

    [ObservableProperty]
    private int _resolutionHeight = 600;

    [ObservableProperty]
    private bool _windowed;

    [ObservableProperty]
    private TextureQuality _textureQuality = TextureQuality.High;

    [ObservableProperty]
    private bool _shadows = true;

    [ObservableProperty]
    private bool _particleEffects = true;

    [ObservableProperty]
    private bool _extraAnimations = true;

    [ObservableProperty]
    private bool _buildingAnimations = true;

    [ObservableProperty]
    private int _gamma = 50;

    [ObservableProperty]
    private bool _alternateMouseSetup;

    [ObservableProperty]
    private bool _heatEffects = true;

    [ObservableProperty]
    private bool _useShadowDecals = true;

    [ObservableProperty]
    private bool _buildingOcclusion = true;

    [ObservableProperty]
    private bool _showProps = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLodSelected))]
    private string _staticGameLOD = "High";

    [ObservableProperty]
    private string _idealStaticGameLOD = "VeryHigh";

    partial void OnStaticGameLODChanged(string value)
    {
        // When user sets LOD to High or VeryHigh, ensure Ideal follows
        if (value == "VeryHigh") IdealStaticGameLOD = "VeryHigh";
        else if (value == "High" && IdealStaticGameLOD == "Medium") IdealStaticGameLOD = "High";
    }

    [ObservableProperty]
    private bool _useDoubleClickAttackMove = true;

    [ObservableProperty]
    private int _scrollFactor = 50;

    [ObservableProperty]
    private bool _retaliation = true;

    [ObservableProperty]
    private bool _dynamicLOD = false;

    [ObservableProperty]
    private int _maxParticleCount = 5000;

    [ObservableProperty]
    private int _antiAliasing = 1;

    [ObservableProperty]
    private bool _drawScrollAnchor = false;

    [ObservableProperty]
    private bool _moveScrollAnchor = true;

    [ObservableProperty]
    private int _gameTimeFontSize = 10;

    [ObservableProperty]
    private bool _languageFilter = false;

    [ObservableProperty]
    private bool _sendDelay = false;

    [ObservableProperty]
    private bool _showSoftWaterEdge = true;

    [ObservableProperty]
    private bool _showTrees = true;

    [ObservableProperty]
    private bool _useCloudMap = true;

    [ObservableProperty]
    private bool _useLightMap = true;

    [ObservableProperty]
    private bool _skipEALogo = false;

    [ObservableProperty]
    private string _colorValue = "#8E44AD";

    [ObservableProperty]
    private ObservableCollection<string> _resolutionPresets = new(ResolutionPresetsProvider.StandardResolutions);

    [ObservableProperty]
    private string? _selectedResolutionPreset;

    [ObservableProperty]
    private ObservableCollection<string> _lodOptions = ["Low", "Medium", "High", "VeryHigh", "Custom"];

    /// <summary>
    /// Gets a value indicating whether the custom LOD option is selected.
    /// </summary>
    public bool IsCustomLodSelected => StaticGameLOD == "Custom";

    [ObservableProperty]
    private ObservableCollection<int> _aaOptions = [1, 2, 4];

    // ===== TheSuperHackers Client Settings =====
    [ObservableProperty]
    private bool _tshArchiveReplays;

    [ObservableProperty]
    private bool _tshShowMoneyPerMinute;

    [ObservableProperty]
    private bool _tshPlayerObserverEnabled = GameSettingsTheSuperHackersConstants.DefaultPlayerObserverEnabled;

    [ObservableProperty]
    private int _tshSystemTimeFontSize = GameSettingsTheSuperHackersConstants.DefaultSystemTimeFontSize;

    [ObservableProperty]
    private int _tshNetworkLatencyFontSize = GameSettingsTheSuperHackersConstants.DefaultNetworkLatencyFontSize;

    [ObservableProperty]
    private int _tshRenderFpsFontSize = GameSettingsTheSuperHackersConstants.DefaultRenderFpsFontSize;

    [ObservableProperty]
    private int _tshResolutionFontAdjustment = GameSettingsTheSuperHackersConstants.DefaultResolutionFontAdjustment;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInFullscreenGame = GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInFullscreenGame;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInFullscreenMenu = GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInFullscreenMenu;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInWindowedGame = GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInWindowedGame;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInWindowedMenu = GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInWindowedMenu;

    [ObservableProperty]
    private bool _tshScreenEdgeScrollEnabledInFullscreenApp = GameSettingsTheSuperHackersConstants.DefaultScreenEdgeScrollEnabledInFullscreenApp;

    [ObservableProperty]
    private bool _tshScreenEdgeScrollEnabledInWindowedApp = GameSettingsTheSuperHackersConstants.DefaultScreenEdgeScrollEnabledInWindowedApp;

    [ObservableProperty]
    private int _tshMoneyTransactionVolume = GameSettingsTheSuperHackersConstants.DefaultMoneyTransactionVolume;

    [ObservableProperty]
    private float _tshGameWindowTransitionSpeedMultiplier = GameSettingsTheSuperHackersConstants.DefaultGameWindowTransitionSpeedMultiplier;

    // ===== GeneralsOnline Client Settings =====
    [ObservableProperty]
    private bool _goShowFps;

    [ObservableProperty]
    private bool _goShowPing = GameSettingsGeneralsOnlineConstants.DefaultShowPing;

    [ObservableProperty]
    private bool _goShowPlayerRanks = GameSettingsGeneralsOnlineConstants.DefaultShowPlayerRanks;

    [ObservableProperty]
    private bool _goAutoLogin;

    [ObservableProperty]
    private bool _goRememberUsername = GameSettingsGeneralsOnlineConstants.DefaultRememberUsername;

    [ObservableProperty]
    private bool _goEnableNotifications = GameSettingsGeneralsOnlineConstants.DefaultEnableNotifications;

    [ObservableProperty]
    private bool _goEnableSoundNotifications = GameSettingsGeneralsOnlineConstants.DefaultEnableSoundNotifications;

    [ObservableProperty]
    private int _goChatFontSize = GameSettingsGeneralsOnlineConstants.DefaultChatFontSize;

    /// <summary>
    /// Gets the minimum chat font size for GeneralsOnline.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to Avalonia view")]
    public int MinGoChatFontSize => GameSettingsGeneralsOnlineConstants.MinChatFontSize;

    /// <summary>
    /// Gets the maximum chat font size for GeneralsOnline.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to Avalonia view")]
    public int MaxGoChatFontSize => GameSettingsGeneralsOnlineConstants.MaxChatFontSize;

    // ===== Custom Camera Settings (Non-GeneralsOnline) =====
    [ObservableProperty]
    private float _cameraHeight = GameSettingsConstants.Camera.DefaultHeight;

    [ObservableProperty]
    private float _cameraMaxHeight = GameSettingsConstants.Camera.DefaultMaxHeight;

    [ObservableProperty]
    private float _cameraMinHeight = GameSettingsConstants.Camera.DefaultMinHeight;

    [ObservableProperty]
    private float _cameraPitch = GameSettingsConstants.Camera.DefaultPitch;

    partial void OnCameraHeightChanged(float value)
    {
        var rounded = MathF.Round(value);
        if (Math.Abs(rounded - value) > 0.001f)
        {
            CameraHeight = rounded;
            return;
        }

        if (CameraHeight > CameraMaxHeight)
        {
            CameraMaxHeight = CameraHeight;
        }

        if (CameraHeight < CameraMinHeight)
        {
            CameraMinHeight = CameraHeight;
        }
    }

    partial void OnCameraMaxHeightChanged(float value)
    {
        var rounded = MathF.Round(value);
        if (Math.Abs(rounded - value) > 0.001f)
        {
            CameraMaxHeight = rounded;
            return;
        }

        if (CameraMaxHeight < CameraMinHeight)
        {
            CameraMinHeight = CameraMaxHeight;
        }

        if (CameraHeight > CameraMaxHeight)
        {
            CameraHeight = CameraMaxHeight;
        }
    }

    partial void OnCameraMinHeightChanged(float value)
    {
        var rounded = MathF.Round(value);
        if (Math.Abs(rounded - value) > 0.001f)
        {
            CameraMinHeight = rounded;
            return;
        }

        if (CameraMinHeight > CameraMaxHeight)
        {
            CameraMaxHeight = CameraMinHeight;
        }

        if (CameraHeight < CameraMinHeight)
        {
            CameraHeight = CameraMinHeight;
        }
    }

    partial void OnCameraPitchChanged(float value)
    {
        var rounded = MathF.Round(value * 2f) / 2f;
        if (Math.Abs(rounded - value) > 0.001f)
        {
            CameraPitch = rounded;
        }
    }

    /// <summary>
    /// Resets camera settings to standard game engine defaults.
    /// </summary>
    [RelayCommand]
    private void ResetCameraDefaults()
    {
        CameraHeight = GameSettingsConstants.Camera.DefaultHeight;
        CameraMaxHeight = GameSettingsConstants.Camera.DefaultMaxHeight;
        CameraMinHeight = GameSettingsConstants.Camera.DefaultMinHeight;
        CameraPitch = GameSettingsConstants.Camera.DefaultPitch;
    }

    // Camera settings
    [ObservableProperty]
    private float _goCameraMaxHeightOnlyWhenLobbyHost = 310.0f;

    [ObservableProperty]
    private float _goCameraMinHeight = 310.0f;

    [ObservableProperty]
    private float _goCameraMoveSpeedRatio = 1.5f;

    partial void OnGoCameraMinHeightChanged(float value)
    {
        var rounded = MathF.Round(value);
        if (Math.Abs(rounded - value) > 0.001f)
        {
            GoCameraMinHeight = rounded;
        }
    }

    partial void OnGoCameraMaxHeightOnlyWhenLobbyHostChanged(float value)
    {
        var rounded = MathF.Round(value);
        if (Math.Abs(rounded - value) > 0.001f)
        {
            GoCameraMaxHeightOnlyWhenLobbyHost = rounded;
        }
    }

    partial void OnGoCameraMoveSpeedRatioChanged(float value)
    {
        var rounded = MathF.Round(value * 10f) / 10f;
        if (Math.Abs(rounded - value) > 0.001f)
        {
            GoCameraMoveSpeedRatio = rounded;
        }
    }

    // Chat settings
    [ObservableProperty]
    private int _goChatDurationSecondsUntilFadeOut = 30;

    // Debug settings
    [ObservableProperty]
    private bool _goDebugVerboseLogging;

    // Render settings
    [ObservableProperty]
    private int _goRenderFpsLimit = 144;

    [ObservableProperty]
    private bool _goRenderLimitFramerate = true;

    [ObservableProperty]
    private bool _goRenderStatsOverlay = true;

    // Social notification settings
    [ObservableProperty]
    private bool _goSocialNotificationFriendComesOnlineGameplay = true;

    [ObservableProperty]
    private bool _goSocialNotificationFriendComesOnlineMenus = true;

    [ObservableProperty]
    private bool _goSocialNotificationFriendGoesOfflineGameplay = true;

    [ObservableProperty]
    private bool _goSocialNotificationFriendGoesOfflineMenus = true;

    [ObservableProperty]
    private bool _goSocialNotificationPlayerAcceptsRequestGameplay = true;

    [ObservableProperty]
    private bool _goSocialNotificationPlayerAcceptsRequestMenus = true;

    [ObservableProperty]
    private bool _goSocialNotificationPlayerSendsRequestGameplay = true;

    [ObservableProperty]
    private bool _goSocialNotificationPlayerSendsRequestMenus = true;

    [ObservableProperty]
    private string? _gameSpyIPAddress;

    /// <summary>
    /// Initializes the ViewModel and loads settings for a specific profile.
    /// </summary>
    /// <param name="profileId">The profile ID to load settings for.</param>
    /// <param name="profile">The game profile with settings.</param>
    /// <param name="initialGameType">The initial game type to select when creating a new profile.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeForProfileAsync(string? profileId, Core.Models.GameProfile.GameProfile? profile = null, GameType? initialGameType = null)
    {
        _initializationDepth++;
        IsLoading = true;  // Provide UI feedback for loading state

        try
        {
            _currentProfileId = profileId;
            _currentProfileIsGeneralsOnline = profile?.IsGeneralsOnlineProfile() == true;
            _generalsOnlineSettingsSeeded = false;

            if (!ResolveInitialGameType(profile, initialGameType, profileId))
            {
                return;
            }

            var optionsLoaded = await SeedBaselineOptionsAsync();
            await SeedProfileOrDefaultsAsync(profile, optionsLoaded);
        }
        finally
        {
            _initializationDepth--;
            IsLoading = false;  // Clear UI loading feedback
        }
    }

    /// <summary>
    /// Gets the current settings as an UpdateProfileRequest for saving to a profile.
    /// </summary>
    /// <returns>An UpdateProfileRequest with the current settings.</returns>
    public Core.Models.GameProfile.UpdateProfileRequest GetProfileSettings()
    {
        return new Core.Models.GameProfile.UpdateProfileRequest
        {
            VideoResolutionWidth = ResolutionWidth,
            VideoResolutionHeight = ResolutionHeight,
            VideoWindowed = Windowed,
            VideoTextureQuality = TextureQuality,
            EnableVideoShadows = Shadows,
            VideoParticleEffects = ParticleEffects,
            VideoExtraAnimations = ExtraAnimations,
            VideoBuildingAnimations = BuildingAnimations,
            VideoGamma = Gamma,
            VideoAlternateMouseSetup = AlternateMouseSetup,
            VideoHeatEffects = HeatEffects,
            VideoUseShadowDecals = UseShadowDecals,
            VideoBuildingOcclusion = BuildingOcclusion,
            VideoShowProps = ShowProps,
            VideoStaticGameLOD = StaticGameLOD,
            VideoIdealStaticGameLOD = IdealStaticGameLOD,
            VideoUseDoubleClickAttackMove = UseDoubleClickAttackMove,
            VideoScrollFactor = ScrollFactor,
            VideoRetaliation = Retaliation,
            VideoDynamicLOD = DynamicLOD,
            VideoMaxParticleCount = MaxParticleCount,
            VideoAntiAliasing = AntiAliasing,
            VideoDrawScrollAnchor = DrawScrollAnchor,
            VideoMoveScrollAnchor = MoveScrollAnchor,
            VideoGameTimeFontSize = GameTimeFontSize,
            GameLanguageFilter = LanguageFilter,
            NetworkSendDelay = SendDelay,
            VideoShowSoftWaterEdge = ShowSoftWaterEdge,
            VideoShowTrees = ShowTrees,
            VideoUseCloudMap = UseCloudMap,
            VideoUseLightMap = UseLightMap,
            AudioSoundVolume = SoundVolume,
            AudioThreeDSoundVolume = ThreeDSoundVolume,
            AudioSpeechVolume = SpeechVolume,
            AudioMusicVolume = MusicVolume,
            AudioEnabled = AudioEnabled,
            AudioNumSounds = NumSounds,
            VideoSkipEALogo = SkipEALogo,

            // TheSuperHackers settings
            TshArchiveReplays = TshArchiveReplays,
            TshShowMoneyPerMinute = TshShowMoneyPerMinute,
            TshPlayerObserverEnabled = TshPlayerObserverEnabled,
            TshSystemTimeFontSize = TshSystemTimeFontSize,
            TshNetworkLatencyFontSize = TshNetworkLatencyFontSize,
            TshRenderFpsFontSize = TshRenderFpsFontSize,
            TshResolutionFontAdjustment = TshResolutionFontAdjustment,
            TshCursorCaptureEnabledInFullscreenGame = TshCursorCaptureEnabledInFullscreenGame,
            TshCursorCaptureEnabledInFullscreenMenu = TshCursorCaptureEnabledInFullscreenMenu,
            TshCursorCaptureEnabledInWindowedGame = TshCursorCaptureEnabledInWindowedGame,
            TshCursorCaptureEnabledInWindowedMenu = TshCursorCaptureEnabledInWindowedMenu,
            TshScreenEdgeScrollEnabledInFullscreenApp = TshScreenEdgeScrollEnabledInFullscreenApp,
            TshScreenEdgeScrollEnabledInWindowedApp = TshScreenEdgeScrollEnabledInWindowedApp,
            TshMoneyTransactionVolume = TshMoneyTransactionVolume,
            TshGameWindowTransitionSpeedMultiplier = GameSettingsMapper.NormalizeTransitionSpeedMultiplier(TshGameWindowTransitionSpeedMultiplier) ?? GameSettingsTheSuperHackersConstants.DefaultGameWindowTransitionSpeedMultiplier,

            // GeneralsOnline settings
            GoShowFps = GoShowFps,
            GoShowPing = GoShowPing,
            GoShowPlayerRanks = GoShowPlayerRanks,
            GoAutoLogin = GoAutoLogin,
            GoRememberUsername = GoRememberUsername,
            GoEnableNotifications = GoEnableNotifications,
            GoEnableSoundNotifications = GoEnableSoundNotifications,
            GoChatFontSize = Math.Clamp(GoChatFontSize, MinGoChatFontSize, MaxGoChatFontSize),

            // Camera settings
            GoCameraMaxHeightOnlyWhenLobbyHost = GoCameraMaxHeightOnlyWhenLobbyHost,
            GoCameraMinHeight = GoCameraMinHeight,
            GoCameraMoveSpeedRatio = GoCameraMoveSpeedRatio,

            // Chat settings
            GoChatDurationSecondsUntilFadeOut = GoChatDurationSecondsUntilFadeOut,

            // Debug settings
            GoDebugVerboseLogging = GoDebugVerboseLogging,

            // Render settings
            GoRenderFpsLimit = GoRenderFpsLimit,
            GoRenderLimitFramerate = GoRenderLimitFramerate,
            GoRenderStatsOverlay = GoRenderStatsOverlay,

            // Social notification settings
            GoSocialNotificationFriendComesOnlineGameplay = GoSocialNotificationFriendComesOnlineGameplay,
            GoSocialNotificationFriendComesOnlineMenus = GoSocialNotificationFriendComesOnlineMenus,
            GoSocialNotificationFriendGoesOfflineGameplay = GoSocialNotificationFriendGoesOfflineGameplay,
            GoSocialNotificationFriendGoesOfflineMenus = GoSocialNotificationFriendGoesOfflineMenus,
            GoSocialNotificationPlayerAcceptsRequestGameplay = GoSocialNotificationPlayerAcceptsRequestGameplay,
            GoSocialNotificationPlayerAcceptsRequestMenus = GoSocialNotificationPlayerAcceptsRequestMenus,
            GoSocialNotificationPlayerSendsRequestGameplay = GoSocialNotificationPlayerSendsRequestGameplay,
            GoSocialNotificationPlayerSendsRequestMenus = GoSocialNotificationPlayerSendsRequestMenus,
            GameSpyIPAddress = GameSpyIPAddress,

            // Camera settings
            CameraHeight = CameraHeight,
            CameraMaxHeight = CameraMaxHeight,
            CameraMinHeight = CameraMinHeight,
            CameraPitch = CameraPitch,
        };
    }

    /// <summary>
    /// Applies a resolution preset.
    /// </summary>
    /// <param name="preset">The resolution preset to apply.</param>
    [RelayCommand]
    public void ApplyResolutionPreset(string? preset)
    {
        if (!TryParseResolution(preset, out var width, out var height))
        {
            StatusMessage = $"Invalid resolution preset: {preset}";
            logger.LogWarning("Failed to parse resolution preset: {Preset}", preset);
            var title = GetWarningTitle();
            var message = GetInvalidResolutionPresetMessage(preset);
            notificationService?.ShowWarning(title, message);
            return;
        }

        ResolutionWidth = width;
        ResolutionHeight = height;
        StatusMessage = $"Resolution set to {width}x{height}";
        var infoTitle = GetResolutionSetTitle();
        var infoMessage = GetResolutionSetMessage(width, height);
        notificationService?.ShowInfo(infoTitle, infoMessage);
    }

    private IniOptions? _currentOptions;
    private bool _generalsOnlineSettingsSeeded;
    private bool _currentProfileIsGeneralsOnline;
    private string? _currentProfileId;
    private int _initializationDepth;
    private volatile bool _isLoadingFromOptions;
    private GameType? _pendingGameTypeLoad;

    /// <summary>
    /// Determines and sets the initial game type during profile initialization.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="initialGameType">The initial game type override.</param>
    /// <param name="profileId">The profile ID.</param>
    /// <returns><see langword="true"/> if game type resolution succeeded and initialization can continue; otherwise, <see langword="false"/>.</returns>
    private bool ResolveInitialGameType(Core.Models.GameProfile.GameProfile? profile, GameType? initialGameType, string? profileId)
    {
        if (profile != null)
        {
            if (profile.IsToolProfile)
            {
                StatusMessage = ProfileValidationConstants.ToolProfileSettingsNotApplicable;
                logger.LogInformation("Skipping settings load for Tool profile {ProfileId}", profileId);
                return false;
            }

            var clientGameType = profile.GameClient?.GameType ?? GameType.Unknown;
            if (clientGameType == GameType.Unknown)
            {
                logger.LogWarning("Cannot initialize settings for profile {Id} with Unknown game type", profile.Id);
                SelectedGameType = GameType.Unknown;
                StatusMessage = "Profile has an unknown game type. Settings cannot be loaded.";
                var title = GetWarningTitle();
                var message = GetUnknownGameTypeMessage();
                notificationService?.ShowWarning(title, message);
                return false;
            }

            SelectedGameType = clientGameType;
            logger.LogInformation(
                "Auto-selected game type {GameType} for profile {ProfileId}",
                SelectedGameType,
                profileId);
            return true;
        }

        if (initialGameType.HasValue && initialGameType.Value != GameType.Unknown)
        {
            SelectedGameType = initialGameType.Value;
            logger.LogInformation("Using initial GameType {GameType} for new profile initialization", SelectedGameType);
            return true;
        }

        logger.LogInformation("Using pre-selected GameType {GameType} for new profile initialization", SelectedGameType);
        return true;
    }

    /// <summary>
    /// Seeds baseline settings and current options from Options.ini.
    /// </summary>
    /// <returns>A task returning <see langword="true"/> if options were loaded successfully; otherwise, <see langword="false"/>.</returns>
    private async Task<bool> SeedBaselineOptionsAsync()
    {
        if (SelectedGameType == GameType.Unknown)
        {
            return false;
        }

        var optionsLoaded = await LoadOptionsFromIniAsync(SelectedGameType);
        if (!optionsLoaded)
        {
            _currentOptions = null;
        }

        return optionsLoaded;
    }

    /// <summary>
    /// Seeds profile custom settings or default GeneralsOnline settings.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="optionsLoaded"><see langword="true"/> if Options.ini options were loaded; otherwise, <see langword="false"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SeedProfileOrDefaultsAsync(Core.Models.GameProfile.GameProfile? profile, bool optionsLoaded)
    {
        if (profile?.HasCustomSettings() == true)
        {
            await LoadGeneralsOnlineSettingsFromClientAsync();
            LoadSettingsFromProfile(profile);
            return;
        }

        if (gameSettingsService != null && _currentProfileIsGeneralsOnline)
        {
            var goResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync();
            if (goResult is { Success: true, Data: not null })
            {
                ApplyGeneralsOnlineSettings(goResult.Data);
                _generalsOnlineSettingsSeeded = true;
            }
        }

        if (optionsLoaded)
        {
            StatusMessage = "Loaded default settings from Options.ini. Save the profile to persist these settings.";
            var title = GetDefaultSettingsLoadedTitle();
            var message = GetDefaultSettingsLoadedMessage();
            notificationService?.ShowInfo(title, message);
        }
    }

    /// <summary>
    /// Reads Options.ini for the specified game type and populates the view model and baseline options.
    /// </summary>
    /// <remarks>
    /// View model properties have no unset state, so all of them are written back on save.
    /// Seeding them from disk is what keeps that from replacing options the profile does not declare with defaults.
    /// Populating <see cref="_currentOptions"/> also preserves unmanaged sections and properties.
    /// </remarks>
    private async Task<bool> LoadOptionsFromIniAsync(GameType gameType)
    {
        if (gameSettingsService == null)
        {
            var title = GetWarningTitle();
            var message = GetServiceUnavailableMessage();
            notificationService?.ShowWarning(title, message);
            return false;
        }

        if (gameType == GameType.Unknown)
        {
            var title = GetWarningTitle();
            var message = GetUnknownGameTypeMessage();
            notificationService?.ShowWarning(title, message);
            return false;
        }

        try
        {
            OptionsFilePath = gameSettingsService.GetOptionsFilePath(gameType);
            OptionsFileExists = gameSettingsService.OptionsFileExists(gameType);

            var result = await gameSettingsService.LoadOptionsAsync(gameType);
            if (gameType != SelectedGameType)
            {
                logger.LogInformation(
                    "Discarding Options.ini load for {GameType} because SelectedGameType changed to {SelectedType}",
                    gameType,
                    SelectedGameType);
                return false;
            }

            if (result is { Success: true, Data: not null })
            {
                _currentOptions = result.Data;
                ApplyOptionsToViewModel(_currentOptions);

                logger.LogInformation("Loaded settings from Options.ini for {GameType}", gameType);
                return true;
            }

            var errors = result?.Errors ?? ["LoadOptions result was null"];
            _currentOptions = null;
            StatusMessage = $"Failed to load settings: {string.Join(", ", errors)}";
            logger.LogWarning("Failed to load Options.ini for {GameType}: {Errors}", gameType, string.Join(", ", errors));
            var title = GetWarningTitle();
            var msgFormat = localizationService?.GetString("GameProfiles.Settings.Notification.LoadFailedMessage") ?? "Failed to load settings: {0}";
            notificationService?.ShowWarning(title, string.Format(CultureInfo.CurrentCulture, msgFormat, string.Join(", ", errors)));
            return false;
        }
        catch (Exception ex)
        {
            if (gameType != SelectedGameType)
            {
                return false;
            }

            _currentOptions = null;
            logger.LogError(ex, "Error loading Options.ini for {GameType}", gameType);
            StatusMessage = $"Error loading settings: {ex.Message}";
            var title = GetErrorLoadingSettingsTitle();
            notificationService?.ShowError(title, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Loads the options.ini settings for the selected game type.
    /// </summary>
    [RelayCommand]
    private async Task LoadSettings()
    {
        try
        {
            IsLoading = true;
            _isLoadingFromOptions = true;

            if (gameSettingsService == null)
            {
                StatusMessage = DefaultServiceUnavailableMessage;
                var title = GetWarningTitle();
                var message = GetServiceUnavailableMessage();
                notificationService?.ShowWarning(title, message);
                return;
            }

            if (SelectedGameType == GameType.Unknown)
            {
                StatusMessage = "Cannot load settings: Game type is Unknown";
                logger.LogWarning("LoadSettings called with Unknown GameType");
                var title = GetWarningTitle();
                var message = GetUnknownGameTypeMessage();
                notificationService?.ShowWarning(title, message);
                return;
            }

            GameType gameType = SelectedGameType;
            StatusMessage = $"Loading {gameType} settings...";

            var loaded = await LoadOptionsFromIniAsync(gameType);
            if (gameType != SelectedGameType)
            {
                return;
            }

            if (loaded)
            {
                StatusMessage = OptionsFileExists
                    ? $"Loaded {gameType} settings from {Path.GetFileName(OptionsFilePath)}"
                    : $"Using default {gameType} settings (file not found)";
            }

            // Load GeneralsOnline settings separately
            var goResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync();
            if (gameType != SelectedGameType)
            {
                return;
            }

            if (goResult is { Success: true, Data: not null })
            {
                ApplyGeneralsOnlineSettings(goResult.Data);
                _generalsOnlineSettingsSeeded = true;
                logger.LogInformation("Loaded GeneralsOnline settings");
            }
            else
            {
                _generalsOnlineSettingsSeeded = false;
                var goErrors = goResult?.Errors ?? ["LoadGeneralsOnlineSettings result was null"];
                logger.LogWarning("Failed to load GeneralsOnline settings: {Errors}", string.Join(", ", goErrors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading settings for {GameType}", SelectedGameType);
            StatusMessage = $"Error loading settings: {ex.Message}";
            var title = GetErrorLoadingSettingsTitle();
            notificationService?.ShowError(title, ex.Message);
        }
        finally
        {
            _isLoadingFromOptions = false;
            IsLoading = false;
            CheckAndDispatchPendingGameTypeLoad();
        }
    }

    /// <summary>
    /// Reads the GeneralsOnline client's own settings.json into this view model.
    /// </summary>
    /// <remarks>
    /// The view model's GeneralsOnline properties have no unset state, so every one of them is
    /// written back on save. Seeding them from the client's file is what keeps that from replacing
    /// options the profile says nothing about with defaults. A read that fails leaves the view
    /// model unseeded, which is what stops the save from writing over the client's own values.
    /// </remarks>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task LoadGeneralsOnlineSettingsFromClientAsync()
    {
        if (gameSettingsService == null || !_currentProfileIsGeneralsOnline)
        {
            return;
        }

        var goResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync();
        if (goResult is { Success: true, Data: not null })
        {
            ApplyGeneralsOnlineSettings(goResult.Data);
            _generalsOnlineSettingsSeeded = true;
        }
        else
        {
            _generalsOnlineSettingsSeeded = false;
            var goErrors = goResult?.Errors ?? ["LoadGeneralsOnlineSettings result was null"];
            logger.LogWarning("Failed to load GeneralsOnline settings: {Errors}", string.Join(", ", goErrors));
        }
    }

    /// <summary>
    /// Loads settings from a game profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    private void LoadSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        logger.LogInformation("Loading settings from profile {ProfileId}", _currentProfileId);

        LoadVideoAudioSettingsFromProfile(profile);
        LoadTshSettingsFromProfile(profile);
        LoadGeneralsOnlineSettingsFromProfile(profile);
        LoadCameraSettingsFromProfile(profile);

        if (profile.GameSpyIPAddress != null) GameSpyIPAddress = profile.GameSpyIPAddress;

        // Update selected preset if it matches
        var currentRes = $"{ResolutionWidth}x{ResolutionHeight}";
        SelectedResolutionPreset = ResolutionPresets.Contains(currentRes) ? currentRes : null;

        StatusMessage = string.Empty;
        logger.LogInformation(
            "Loaded profile settings - Windowed={Windowed}, Resolution={Width}x{Height}",
            Windowed,
            ResolutionWidth,
            ResolutionHeight);
    }

    private void LoadVideoAudioSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        LoadVideoBasicSettingsFromProfile(profile);
        LoadVideoAdvancedSettingsFromProfile(profile);
        LoadAudioSettingsFromProfile(profile);
    }

    private void LoadVideoBasicSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.VideoResolutionWidth.HasValue) ResolutionWidth = profile.VideoResolutionWidth.Value;
        if (profile.VideoResolutionHeight.HasValue) ResolutionHeight = profile.VideoResolutionHeight.Value;
        if (profile.VideoWindowed.HasValue) Windowed = profile.VideoWindowed.Value;
        if (profile.VideoTextureQuality.HasValue) TextureQuality = profile.VideoTextureQuality.Value;
        if (profile.EnableVideoShadows.HasValue) Shadows = profile.EnableVideoShadows.Value;
        if (profile.VideoParticleEffects.HasValue) ParticleEffects = profile.VideoParticleEffects.Value;
        if (profile.VideoExtraAnimations.HasValue) ExtraAnimations = profile.VideoExtraAnimations.Value;
        if (profile.VideoBuildingAnimations.HasValue) BuildingAnimations = profile.VideoBuildingAnimations.Value;
        if (profile.VideoGamma.HasValue) Gamma = profile.VideoGamma.Value;
        if (profile.VideoAlternateMouseSetup.HasValue) AlternateMouseSetup = profile.VideoAlternateMouseSetup.Value;
        if (profile.VideoHeatEffects.HasValue) HeatEffects = profile.VideoHeatEffects.Value;
        if (profile.VideoUseShadowDecals.HasValue) UseShadowDecals = profile.VideoUseShadowDecals.Value;
        if (profile.VideoBuildingOcclusion.HasValue) BuildingOcclusion = profile.VideoBuildingOcclusion.Value;
        if (profile.VideoShowProps.HasValue) ShowProps = profile.VideoShowProps.Value;
    }

    private void LoadVideoAdvancedSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.VideoStaticGameLOD != null) StaticGameLOD = profile.VideoStaticGameLOD;
        if (profile.VideoIdealStaticGameLOD != null) IdealStaticGameLOD = profile.VideoIdealStaticGameLOD;
        if (profile.VideoUseDoubleClickAttackMove.HasValue) UseDoubleClickAttackMove = profile.VideoUseDoubleClickAttackMove.Value;
        if (profile.VideoScrollFactor.HasValue) ScrollFactor = profile.VideoScrollFactor.Value;
        if (profile.VideoRetaliation.HasValue) Retaliation = profile.VideoRetaliation.Value;
        if (profile.VideoDynamicLOD.HasValue) DynamicLOD = profile.VideoDynamicLOD.Value;
        if (profile.VideoMaxParticleCount.HasValue) MaxParticleCount = profile.VideoMaxParticleCount.Value;
        if (profile.VideoAntiAliasing.HasValue) AntiAliasing = profile.VideoAntiAliasing.Value;
        if (profile.VideoDrawScrollAnchor.HasValue) DrawScrollAnchor = profile.VideoDrawScrollAnchor.Value;
        if (profile.VideoMoveScrollAnchor.HasValue) MoveScrollAnchor = profile.VideoMoveScrollAnchor.Value;
        if (profile.VideoGameTimeFontSize.HasValue) GameTimeFontSize = profile.VideoGameTimeFontSize.Value;
        if (profile.GameLanguageFilter.HasValue) LanguageFilter = profile.GameLanguageFilter.Value;
        if (profile.NetworkSendDelay.HasValue) SendDelay = profile.NetworkSendDelay.Value;
        if (profile.VideoShowSoftWaterEdge.HasValue) ShowSoftWaterEdge = profile.VideoShowSoftWaterEdge.Value;
        if (profile.VideoShowTrees.HasValue) ShowTrees = profile.VideoShowTrees.Value;
        if (profile.VideoUseCloudMap.HasValue) UseCloudMap = profile.VideoUseCloudMap.Value;
        if (profile.VideoUseLightMap.HasValue) UseLightMap = profile.VideoUseLightMap.Value;
        if (profile.VideoSkipEALogo.HasValue) SkipEALogo = profile.VideoSkipEALogo.Value;
    }

    private void LoadAudioSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.AudioSoundVolume.HasValue) SoundVolume = profile.AudioSoundVolume.Value;
        if (profile.AudioThreeDSoundVolume.HasValue) ThreeDSoundVolume = profile.AudioThreeDSoundVolume.Value;
        if (profile.AudioSpeechVolume.HasValue) SpeechVolume = profile.AudioSpeechVolume.Value;
        if (profile.AudioMusicVolume.HasValue) MusicVolume = profile.AudioMusicVolume.Value;
        if (profile.AudioEnabled.HasValue) AudioEnabled = profile.AudioEnabled.Value;
        if (profile.AudioNumSounds.HasValue) NumSounds = profile.AudioNumSounds.Value;
    }

    private void LoadTshSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.TshArchiveReplays.HasValue) TshArchiveReplays = profile.TshArchiveReplays.Value;
        if (profile.TshShowMoneyPerMinute.HasValue) TshShowMoneyPerMinute = profile.TshShowMoneyPerMinute.Value;
        if (profile.TshPlayerObserverEnabled.HasValue) TshPlayerObserverEnabled = profile.TshPlayerObserverEnabled.Value;
        if (profile.TshSystemTimeFontSize.HasValue) TshSystemTimeFontSize = profile.TshSystemTimeFontSize.Value;
        if (profile.TshNetworkLatencyFontSize.HasValue) TshNetworkLatencyFontSize = profile.TshNetworkLatencyFontSize.Value;
        if (profile.TshRenderFpsFontSize.HasValue) TshRenderFpsFontSize = profile.TshRenderFpsFontSize.Value;
        if (profile.TshResolutionFontAdjustment.HasValue) TshResolutionFontAdjustment = profile.TshResolutionFontAdjustment.Value;
        if (profile.TshCursorCaptureEnabledInFullscreenGame.HasValue) TshCursorCaptureEnabledInFullscreenGame = profile.TshCursorCaptureEnabledInFullscreenGame.Value;
        if (profile.TshCursorCaptureEnabledInFullscreenMenu.HasValue) TshCursorCaptureEnabledInFullscreenMenu = profile.TshCursorCaptureEnabledInFullscreenMenu.Value;
        if (profile.TshCursorCaptureEnabledInWindowedGame.HasValue) TshCursorCaptureEnabledInWindowedGame = profile.TshCursorCaptureEnabledInWindowedGame.Value;
        if (profile.TshCursorCaptureEnabledInWindowedMenu.HasValue) TshCursorCaptureEnabledInWindowedMenu = profile.TshCursorCaptureEnabledInWindowedMenu.Value;
        if (profile.TshScreenEdgeScrollEnabledInFullscreenApp.HasValue) TshScreenEdgeScrollEnabledInFullscreenApp = profile.TshScreenEdgeScrollEnabledInFullscreenApp.Value;
        if (profile.TshScreenEdgeScrollEnabledInWindowedApp.HasValue) TshScreenEdgeScrollEnabledInWindowedApp = profile.TshScreenEdgeScrollEnabledInWindowedApp.Value;
        if (profile.TshMoneyTransactionVolume.HasValue) TshMoneyTransactionVolume = profile.TshMoneyTransactionVolume.Value;
        if (GameSettingsMapper.NormalizeTransitionSpeedMultiplier(profile.TshGameWindowTransitionSpeedMultiplier) is { } speedVal)
        {
            TshGameWindowTransitionSpeedMultiplier = speedVal;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates generated observable properties in partial view model")]
    private void LoadCameraSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        CameraHeight = MathF.Round(profile.CameraHeight ?? GameSettingsConstants.Camera.DefaultHeight);
        CameraMaxHeight = MathF.Round(profile.CameraMaxHeight ?? GameSettingsConstants.Camera.DefaultMaxHeight);
        CameraMinHeight = MathF.Round(profile.CameraMinHeight ?? GameSettingsConstants.Camera.DefaultMinHeight);
        CameraPitch = MathF.Round((profile.CameraPitch ?? GameSettingsConstants.Camera.DefaultPitch) * 2f) / 2f;
    }

    private void LoadGeneralsOnlineSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.GoShowFps.HasValue) GoShowFps = profile.GoShowFps.Value;
        if (profile.GoShowPing.HasValue) GoShowPing = profile.GoShowPing.Value;
        if (profile.GoShowPlayerRanks.HasValue) GoShowPlayerRanks = profile.GoShowPlayerRanks.Value;
        if (profile.GoAutoLogin.HasValue) GoAutoLogin = profile.GoAutoLogin.Value;
        if (profile.GoRememberUsername.HasValue) GoRememberUsername = profile.GoRememberUsername.Value;
        if (profile.GoEnableNotifications.HasValue) GoEnableNotifications = profile.GoEnableNotifications.Value;
        if (profile.GoEnableSoundNotifications.HasValue) GoEnableSoundNotifications = profile.GoEnableSoundNotifications.Value;
        if (profile.GoChatFontSize.HasValue) GoChatFontSize = Math.Clamp(profile.GoChatFontSize.Value, MinGoChatFontSize, MaxGoChatFontSize);

        // Camera settings
        if (profile.GoCameraMaxHeightOnlyWhenLobbyHost.HasValue) GoCameraMaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost.Value;
        if (profile.GoCameraMinHeight.HasValue) GoCameraMinHeight = profile.GoCameraMinHeight.Value;
        if (profile.GoCameraMoveSpeedRatio.HasValue) GoCameraMoveSpeedRatio = profile.GoCameraMoveSpeedRatio.Value;

        // Chat settings
        if (profile.GoChatDurationSecondsUntilFadeOut.HasValue) GoChatDurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut.Value;

        // Debug settings
        if (profile.GoDebugVerboseLogging.HasValue) GoDebugVerboseLogging = profile.GoDebugVerboseLogging.Value;

        // Render settings
        if (profile.GoRenderFpsLimit.HasValue) GoRenderFpsLimit = profile.GoRenderFpsLimit.Value;
        if (profile.GoRenderLimitFramerate.HasValue) GoRenderLimitFramerate = profile.GoRenderLimitFramerate.Value;
        if (profile.GoRenderStatsOverlay.HasValue) GoRenderStatsOverlay = profile.GoRenderStatsOverlay.Value;

        // Social notification settings
        if (profile.GoSocialNotificationFriendComesOnlineGameplay.HasValue) GoSocialNotificationFriendComesOnlineGameplay = profile.GoSocialNotificationFriendComesOnlineGameplay.Value;
        if (profile.GoSocialNotificationFriendComesOnlineMenus.HasValue) GoSocialNotificationFriendComesOnlineMenus = profile.GoSocialNotificationFriendComesOnlineMenus.Value;
        if (profile.GoSocialNotificationFriendGoesOfflineGameplay.HasValue) GoSocialNotificationFriendGoesOfflineGameplay = profile.GoSocialNotificationFriendGoesOfflineGameplay.Value;
        if (profile.GoSocialNotificationFriendGoesOfflineMenus.HasValue) GoSocialNotificationFriendGoesOfflineMenus = profile.GoSocialNotificationFriendGoesOfflineMenus.Value;
        if (profile.GoSocialNotificationPlayerAcceptsRequestGameplay.HasValue) GoSocialNotificationPlayerAcceptsRequestGameplay = profile.GoSocialNotificationPlayerAcceptsRequestGameplay.Value;
        if (profile.GoSocialNotificationPlayerAcceptsRequestMenus.HasValue) GoSocialNotificationPlayerAcceptsRequestMenus = profile.GoSocialNotificationPlayerAcceptsRequestMenus.Value;
        if (profile.GoSocialNotificationPlayerSendsRequestGameplay.HasValue) GoSocialNotificationPlayerSendsRequestGameplay = profile.GoSocialNotificationPlayerSendsRequestGameplay.Value;
        if (profile.GoSocialNotificationPlayerSendsRequestMenus.HasValue) GoSocialNotificationPlayerSendsRequestMenus = profile.GoSocialNotificationPlayerSendsRequestMenus.Value;
    }

    /// <summary>
    /// Saves the current settings to Options.ini and, for a GeneralsOnline profile, to the client's
    /// settings.json.
    /// </summary>
    /// <remarks>
    /// The two files are separate writes with no transaction between them, so either one can land
    /// while the other does not: the settings.json rewrite can be refused after Options.ini is
    /// written, and Options.ini can fail after settings.json has been rewritten. Reordering the
    /// writes only moves which half is exposed, so the status message names the halves separately
    /// instead of reporting a total failure over a file that was written.
    /// </remarks>
    [RelayCommand]
    private async Task SaveSettings()
    {
        if (gameSettingsService == null)
        {
            StatusMessage = DefaultServiceUnavailableMessage;
            var title = GetWarningTitle();
            var message = GetServiceUnavailableMessage();
            notificationService?.ShowWarning(title, message);
            return;
        }

        if (OptionsFileExists && _currentOptions == null)
        {
            StatusMessage = "Cannot save settings: Options.ini could not be loaded and saving would overwrite existing unmanaged settings.";
            logger.LogWarning("Aborting SaveSettings for {GameType} because Options.ini exists but _currentOptions is null", SelectedGameType);
            var title = localizationService?.GetString("GameProfiles.Settings.Notification.CannotSaveSettingsTitle") ?? "Cannot Save Settings";
            var message = localizationService?.GetString("GameProfiles.Settings.Notification.CannotSaveOptionsMissingMessage") ?? "Options.ini could not be loaded and saving would overwrite existing unmanaged settings.";
            notificationService?.ShowWarning(title, message);
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Saving {SelectedGameType} settings...";

            var options = CreateOptionsFromViewModel();
            var result = await gameSettingsService.SaveOptionsAsync(SelectedGameType, options);

            var writeGeneralsOnlineSettings = ShouldWriteGeneralsOnlineSettings();
            OperationResult<bool>? goResult = null;
            string? goLoadError = null;

            if (writeGeneralsOnlineSettings)
            {
                var goLoadResult = await ReadGeneralsOnlineSettingsForRewriteAsync();
                if (goLoadResult.Success && goLoadResult.Data != null)
                {
                    var goSettings = goLoadResult.Data;
                    MergeViewModelIntoGeneralsOnlineSettings(goSettings);
                    goResult = await gameSettingsService.SaveGeneralsOnlineSettingsAsync(goSettings);
                }
                else
                {
                    goLoadError = goLoadResult.FirstError;
                }
            }

            var optionsSaved = result is { Success: true };
            var generalsOnlineWritten = goResult is { Success: true };
            var generalsOnlineBlocked = writeGeneralsOnlineSettings && !generalsOnlineWritten;

            if (optionsSaved)
            {
                _currentOptions = options;
                OptionsFileExists = true;
            }

            var optionsErrors = new List<string>();
            if (result is null)
            {
                optionsErrors.Add("SaveOptions result was null");
            }
            else if (result is { Success: false })
            {
                optionsErrors.AddRange(result.Errors);
            }

            var generalsOnlineErrors = new List<string>();
            if (goLoadError != null)
            {
                generalsOnlineErrors.Add(goLoadError);
            }

            if (goResult is { Success: false })
            {
                generalsOnlineErrors.AddRange(goResult.Errors);
            }

            if (generalsOnlineBlocked && goLoadError == null && goResult == null)
            {
                generalsOnlineErrors.Add("SaveGeneralsOnlineSettings result was null");
            }

            if (optionsSaved && !generalsOnlineBlocked)
            {
                StatusMessage = $"{SelectedGameType} settings saved successfully";
                logger.LogInformation("Saved settings for {GameType}", SelectedGameType);
                var title = GetSettingsSavedTitle();
                var message = GetSettingsSavedMessage(SelectedGameType);
                notificationService?.ShowSuccess(title, message);
            }
            else if (optionsSaved)
            {
                var goErrors = string.Join(", ", generalsOnlineErrors);
                StatusMessage = $"Options.ini saved; GeneralsOnline settings not written: {goErrors}";
                logger.LogWarning("Saved Options.ini for {GameType} but did not write GeneralsOnline settings: {Errors}", SelectedGameType, goErrors);
                var title = GetWarningTitle();
                var msgFormat = localizationService?.GetString("GameProfiles.Settings.Notification.PartialSaveOptionsOnlyMessage") ?? "Options.ini saved; GeneralsOnline settings not written: {0}";
                notificationService?.ShowWarning(title, string.Format(CultureInfo.CurrentCulture, msgFormat, goErrors));
            }
            else if (generalsOnlineWritten)
            {
                var iniErrors = string.Join(", ", optionsErrors);
                StatusMessage = $"GeneralsOnline settings saved; Options.ini not saved: {iniErrors}";
                logger.LogWarning("Wrote GeneralsOnline settings but failed to save Options.ini for {GameType}: {Errors}", SelectedGameType, iniErrors);
                var title = GetWarningTitle();
                var msgFormat = localizationService?.GetString("GameProfiles.Settings.Notification.PartialSaveGeneralsOnlineOnlyMessage") ?? "GeneralsOnline settings saved; Options.ini not saved: {0}";
                notificationService?.ShowWarning(title, string.Format(CultureInfo.CurrentCulture, msgFormat, iniErrors));
            }
            else
            {
                var errors = string.Join(", ", optionsErrors.Concat(generalsOnlineErrors));
                StatusMessage = $"Failed to save settings: {errors}";
                logger.LogWarning("Failed to save settings: {Errors}", errors);
                var title = GetErrorSavingSettingsTitle();
                notificationService?.ShowError(title, errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving settings for {GameType}", SelectedGameType);
            StatusMessage = $"Error saving settings: {ex.Message}";
            var title = GetErrorSavingSettingsTitle();
            notificationService?.ShowError(title, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Reads the GeneralsOnline client's settings.json so the save can be applied on top of it.
    /// </summary>
    /// <remarks>
    /// The file is read again for every save rather than kept as a snapshot: it is the
    /// GeneralsOnline client's own global file, so anything it or another GenHub window wrote
    /// since this editor opened would otherwise be reverted by the rewrite. Reading it is also
    /// the only way to fail loudly, because a missing file reads as defaults and reports success:
    /// a failure therefore means the client's file exists and could not be read, and rewriting it
    /// from defaults would discard every key the client owns.
    /// <para>
    /// The read alone is not enough. This view model has no unset state, so it writes all 24
    /// GeneralsOnline fields; unless they were seeded from a successful read, writing them would
    /// replace what the user configured inside the client with this view model's defaults.
    /// </para>
    /// </remarks>
    /// <returns>The settings this save must be applied on top of, or the error that aborts the rewrite.</returns>
    private async Task<OperationResult<GeneralsOnlineSettings>> ReadGeneralsOnlineSettingsForRewriteAsync()
    {
        if (!_generalsOnlineSettingsSeeded)
        {
            const string error = "GeneralsOnline settings.json was never read, so its values cannot be rewritten";
            logger.LogWarning("Not writing GeneralsOnline settings: {Error}", error);
            return OperationResult<GeneralsOnlineSettings>.CreateFailure(error);
        }

        var goLoadResult = await gameSettingsService!.LoadGeneralsOnlineSettingsAsync();
        if (goLoadResult is { Success: true, Data: not null })
        {
            return goLoadResult;
        }

        var loadError = goLoadResult?.FirstError ?? "LoadGeneralsOnlineSettings result was null";
        logger.LogWarning("Not writing GeneralsOnline settings because settings.json could not be read: {Error}", loadError);
        return OperationResult<GeneralsOnlineSettings>.CreateFailure(loadError);
    }

    /// <summary>
    /// Opens the Options.ini file location in Windows Explorer.
    /// </summary>
    [RelayCommand]
    private void OpenFileLocation()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(OptionsFilePath);
            if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PlatformConstants.WindowsExplorerPath,
                    Arguments = directory,
                    UseShellExecute = true,
                });
                logger.LogInformation("Opened file location {Directory}", directory);
            }
            else
            {
                StatusMessage = "Options file directory not found";
                var title = localizationService?.GetString("GameProfiles.Settings.Notification.DirectoryNotFoundTitle") ?? "Directory Not Found";
                var message = localizationService?.GetString("GameProfiles.Settings.Notification.DirectoryNotFoundMessage") ?? "Options file directory not found";
                notificationService?.ShowWarning(title, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening file location");
            StatusMessage = $"Error opening location: {ex.Message}";
            var title = GetErrorOpeningLocationTitle();
            var message = GetErrorOpeningLocationMessage();
            notificationService?.ShowError(title, message);
        }
    }

    partial void OnSelectedResolutionPresetChanged(string? value)
    {
        if (_initializationDepth > 0 || _isLoadingFromOptions)
        {
            return;
        }

        if (!string.IsNullOrEmpty(value))
        {
            ApplyResolutionPreset(value);
        }
    }

    partial void OnSelectedGameTypeChanged(GameType value)
    {
        if (_initializationDepth > 0)
        {
            logger.LogInformation("GameType set to {GameType} during initialization - skipping auto-load", value);
            return;
        }

        if (value == GameType.Unknown)
        {
            logger.LogInformation("GameType set to Unknown - skipping auto-load");
            return;
        }

        if (_isLoadingFromOptions)
        {
            _pendingGameTypeLoad = value;
            logger.LogInformation("GameType changed to {GameType} while loading from Options.ini - scheduled pending reload", value);
            return;
        }

        TriggerAutoLoadSettings(value);
    }

    private void TriggerAutoLoadSettings(GameType value)
    {
        logger.LogInformation("GameType changed to {GameType} - loading from Options.ini", value);
        _isLoadingFromOptions = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadSettingsCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load settings for {GameType}", value);
                StatusMessage = $"Error loading settings: {ex.Message}";
                var title = GetErrorLoadingSettingsTitle();
                notificationService?.ShowError(title, ex.Message);
            }
        });
    }

    private void CheckAndDispatchPendingGameTypeLoad()
    {
        if (_pendingGameTypeLoad.HasValue)
        {
            var pending = _pendingGameTypeLoad.Value;
            _pendingGameTypeLoad = null;
            if (pending != SelectedGameType)
            {
                SelectedGameType = pending;
            }
            else if (pending != GameType.Unknown)
            {
                TriggerAutoLoadSettings(pending);
            }
        }
    }

    private void ApplyOptionsToViewModel(IniOptions options)
    {
        // Audio settings - map from Options.ini names to ViewModel friendly names
        SoundVolume = options.Audio.SFXVolume;
        ThreeDSoundVolume = options.Audio.SFX3DVolume;
        SpeechVolume = options.Audio.VoiceVolume;
        MusicVolume = options.Audio.MusicVolume;
        AudioEnabled = options.Audio.AudioEnabled;
        NumSounds = options.Audio.NumSounds;

        // Video settings
        ResolutionWidth = options.Video.ResolutionWidth;
        ResolutionHeight = options.Video.ResolutionHeight;
        Windowed = options.Video.Windowed;

        // Map TextureReduction (0-3, inverted) to TextureQuality
        var rawTextureReduction = options.Video.TextureReduction;
        var calculatedQuality = (TextureQuality)Math.Clamp(TextureReductionOffset - rawTextureReduction, 0, (int)TextureQuality.VeryHigh);

        logger.LogInformation(
            "Mapping TextureQuality: Options.TR={TR}, Offset={Offset}, Calc={Calc}, Final={Final}",
            rawTextureReduction,
            TextureReductionOffset,
            TextureReductionOffset - rawTextureReduction,
            calculatedQuality);

        TextureQuality = calculatedQuality;
        Shadows = options.Video.UseShadowVolumes;
        UseShadowDecals = options.Video.UseShadowDecals;
        BuildingOcclusion = options.Video.BuildingOcclusion;
        ShowProps = options.Video.ShowProps;

        ApplyVideoAdditionalProperties(options);
        AntiAliasing = options.Video.AntiAliasing;
        ApplyTshAdditionalProperties(options);

        ExtraAnimations = options.Video.ExtraAnimations;
        Gamma = options.Video.Gamma;
        AlternateMouseSetup = options.Video.AlternateMouseSetup;
        HeatEffects = options.Video.HeatEffects;

        GameSpyIPAddress = options.Network.GameSpyIPAddress;

        // Update selected preset if it matches
        var currentRes = $"{ResolutionWidth}x{ResolutionHeight}";
        SelectedResolutionPreset = ResolutionPresets.Contains(currentRes) ? currentRes : null;
    }

    private void ApplyVideoAdditionalProperties(IniOptions options)
    {
        if (options.Video.AdditionalProperties.TryGetValue("GenHubParticleEffects", out var particleEffects))
            ParticleEffects = ParseBool(particleEffects);
        if (options.Video.AdditionalProperties.TryGetValue("GenHubBuildingAnimations", out var buildingAnimations))
            BuildingAnimations = ParseBool(buildingAnimations);

        if (options.Video.AdditionalProperties.TryGetValue("StaticGameLOD", out var staticLOD))
            StaticGameLOD = staticLOD;
        if (options.Video.AdditionalProperties.TryGetValue("IdealStaticGameLOD", out var idealLOD))
            IdealStaticGameLOD = idealLOD;

        if (options.Video.AdditionalProperties.TryGetValue("ShowSoftWaterEdge", out var swe)) ShowSoftWaterEdge = ParseBool(swe);
        if (options.Video.AdditionalProperties.TryGetValue("ShowTrees", out var st)) ShowTrees = ParseBool(st);
        if (options.Video.AdditionalProperties.TryGetValue("UseCloudMap", out var ucm)) UseCloudMap = ParseBool(ucm);
        if (options.Video.AdditionalProperties.TryGetValue("UseLightMap", out var ulm)) UseLightMap = ParseBool(ulm);

        if (GetAnySettingValue(options, GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey) is { } draws) DrawScrollAnchor = ParseBool(draws);
        if (GetAnySettingValue(options, GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey) is { } moves) MoveScrollAnchor = ParseBool(moves);
        if (GetAnySettingValue(options, GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey) is { } gtfs && int.TryParse(gtfs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gtfsVal)) GameTimeFontSize = gtfsVal;
        if (GetAnySettingValue(options, GameSettingsTheSuperHackersConstants.LanguageFilterKey) is { } lf) LanguageFilter = ParseBool(lf);
        if (GetAnySettingValue(options, GameSettingsTheSuperHackersConstants.SendDelayKey) is { } sd) SendDelay = ParseBool(sd);
        if (GetAnySettingValue(options, "SkipEALogo") is { } sel) SkipEALogo = ParseBool(sel);
    }

    private void ApplyTshAdditionalProperties(IniOptions options)
    {
        var tshKvp = options.AdditionalSections.FirstOrDefault(s =>
            string.Equals(s.Key, GameSettingsTheSuperHackersConstants.SectionName, StringComparison.OrdinalIgnoreCase));

        ApplyTshGameplayProperties(tshKvp.Value, options.Video.AdditionalProperties);
        if (tshKvp.Value != null)
        {
            ApplyTshUiCursorProperties(tshKvp.Value);
        }
    }

    private void ApplyTshGameplayProperties(Dictionary<string, string>? tsh, Dictionary<string, string>? videoAdditional)
    {
        ApplySharedEngineProperties(tsh, videoAdditional);
        if (tsh != null)
        {
            ApplyTshExclusiveGameplayProperties(tsh);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit generated instance properties in partial view model")]
    private void ApplySharedEngineProperties(Dictionary<string, string>? tsh, Dictionary<string, string>? videoAdditional)
    {
        var doubleClick = GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.UseDoubleClickAttackMoveKey)
            ?? GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.UseDoubleClickKey);
        if (doubleClick != null)
            UseDoubleClickAttackMove = ParseBool(doubleClick);

        var scroll = GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.ScrollFactorKey);
        if (scroll != null && int.TryParse(scroll, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scrollVal))
            ScrollFactor = scrollVal;

        var retaliation = GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.RetaliationKey);
        if (retaliation != null)
            Retaliation = ParseBool(retaliation);

        var dynLOD = GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.DynamicLODKey);
        if (dynLOD != null)
            DynamicLOD = ParseBool(dynLOD);

        var particles = GetFirstSettingValue(tsh, videoAdditional, GameSettingsTheSuperHackersConstants.MaxParticleCountKey);
        if (particles != null && int.TryParse(particles, NumberStyles.Integer, CultureInfo.InvariantCulture, out var particleVal))
            MaxParticleCount = particleVal;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit generated instance properties in partial view model")]
    private void ApplyTshExclusiveGameplayProperties(Dictionary<string, string> tsh)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ArchiveReplaysKey, out var ar)) TshArchiveReplays = ParseBool(ar);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ShowMoneyPerMinuteKey, out var smpm)) TshShowMoneyPerMinute = ParseBool(smpm);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.PlayerObserverEnabledKey, out var poe)) TshPlayerObserverEnabled = ParseBool(poe);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.MoneyTransactionVolumeKey, out var mtv) && int.TryParse(mtv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mtvVal)) TshMoneyTransactionVolume = mtvVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.GameWindowTransitionSpeedMultiplierKey, out var gwt))
        {
            var parsed = GameSettingsMapper.ParseTransitionSpeedMultiplier(gwt);
            if (parsed.HasValue)
            {
                TshGameWindowTransitionSpeedMultiplier = parsed.Value;
            }
        }
    }

    private void ApplyTshUiCursorProperties(Dictionary<string, string> tsh)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.SystemTimeFontSizeKey, out var stfs) && int.TryParse(stfs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stfsVal)) TshSystemTimeFontSize = stfsVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.NetworkLatencyFontSizeKey, out var nlfs) && int.TryParse(nlfs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nlfsVal)) TshNetworkLatencyFontSize = nlfsVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.RenderFpsFontSizeKey, out var rffs) && int.TryParse(rffs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rffsVal)) TshRenderFpsFontSize = rffsVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ResolutionFontAdjustmentKey, out var rfa) && int.TryParse(rfa, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rfaVal)) TshResolutionFontAdjustment = rfaVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenGameKey, out var ccefg)) TshCursorCaptureEnabledInFullscreenGame = ParseBool(ccefg);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenMenuKey, out var ccefm)) TshCursorCaptureEnabledInFullscreenMenu = ParseBool(ccefm);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedGameKey, out var ccewg)) TshCursorCaptureEnabledInWindowedGame = ParseBool(ccewg);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedMenuKey, out var ccewm)) TshCursorCaptureEnabledInWindowedMenu = ParseBool(ccewm);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInFullscreenAppKey, out var scrollApp)) TshScreenEdgeScrollEnabledInFullscreenApp = ParseBool(scrollApp);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInWindowedAppKey, out var scrollWinApp)) TshScreenEdgeScrollEnabledInWindowedApp = ParseBool(scrollWinApp);
    }

    private IniOptions CreateOptionsFromViewModel()
    {
        var options = _currentOptions ?? new IniOptions();

        // Audio settings - map from ViewModel friendly names to Options.ini names
        options.Audio.SFXVolume = SoundVolume;
        options.Audio.SFX3DVolume = ThreeDSoundVolume;
        options.Audio.VoiceVolume = SpeechVolume;
        options.Audio.MusicVolume = MusicVolume;
        options.Audio.AudioEnabled = AudioEnabled;
        options.Audio.NumSounds = NumSounds;

        // Video settings (Standard root)
        options.Video.ResolutionWidth = ResolutionWidth;
        options.Video.ResolutionHeight = ResolutionHeight;
        options.Video.Windowed = Windowed;
        options.Video.AntiAliasing = AntiAliasing;

        // Map TextureQuality to TextureReduction (0-3, inverted)
        // Clamp to 0-2 range for Options.ini compatibility
        options.Video.TextureReduction = Math.Clamp(TextureReductionOffset - (int)TextureQuality, 0, 2);
        options.Video.UseShadowVolumes = Shadows;
        options.Video.UseShadowDecals = UseShadowDecals;
        options.Video.BuildingOcclusion = BuildingOcclusion;
        options.Video.ShowProps = ShowProps;

        // Custom GenHub properties
        options.Video.AdditionalProperties["GenHubParticleEffects"] = BoolToString(ParticleEffects);
        options.Video.AdditionalProperties["GenHubBuildingAnimations"] = BoolToString(BuildingAnimations);

        options.Video.AdditionalProperties["ShowSoftWaterEdge"] = BoolToString(ShowSoftWaterEdge);
        options.Video.AdditionalProperties["ShowTrees"] = BoolToString(ShowTrees);
        options.Video.AdditionalProperties["UseCloudMap"] = BoolToString(UseCloudMap);
        options.Video.AdditionalProperties["UseLightMap"] = BoolToString(UseLightMap);
        options.Video.AdditionalProperties["StaticGameLOD"] = StaticGameLOD;
        options.Video.AdditionalProperties["IdealStaticGameLOD"] = IdealStaticGameLOD;
        options.Video.AdditionalProperties["SkipEALogo"] = BoolToString(SkipEALogo);

        // TSH settings (writing to root for maximum compatibility as some clients prefer flat Options.ini)
        options.Video.AdditionalProperties["UseDoubleClickAttackMove"] = BoolToString(UseDoubleClickAttackMove);
        options.Video.AdditionalProperties["ScrollFactor"] = ScrollFactor.ToString(CultureInfo.InvariantCulture);
        options.Video.AdditionalProperties["Retaliation"] = BoolToString(Retaliation);
        options.Video.AdditionalProperties["DynamicLOD"] = BoolToString(DynamicLOD);
        options.Video.AdditionalProperties["MaxParticleCount"] = MaxParticleCount.ToString(CultureInfo.InvariantCulture);
        options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey] = BoolToString(DrawScrollAnchor);
        options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey] = BoolToString(MoveScrollAnchor);
        options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey] = GameTimeFontSize.ToString(CultureInfo.InvariantCulture);
        options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.LanguageFilterKey] = BoolToString(LanguageFilter);
        options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.SendDelayKey] = BoolToString(SendDelay);

        options.Video.ExtraAnimations = ExtraAnimations;
        options.Video.Gamma = Gamma;
        options.Video.AlternateMouseSetup = AlternateMouseSetup;
        options.Video.HeatEffects = HeatEffects;

        // Mirror keys for some TSH client versions
        options.Video.AdditionalProperties["UseAlternateMouse"] = BoolToString(AlternateMouseSetup);
        options.Video.AdditionalProperties["UseDoubleClick"] = BoolToString(UseDoubleClickAttackMove);

        options.Network.GameSpyIPAddress = GameSpyIPAddress;

        // TheSuperHackers settings - preserve existing settings, only update the ones we manage
        var tshKey = options.AdditionalSections.Keys.FirstOrDefault(k =>
            string.Equals(k, GameSettingsTheSuperHackersConstants.SectionName, StringComparison.OrdinalIgnoreCase)) ?? GameSettingsTheSuperHackersConstants.SectionName;

        if (!options.AdditionalSections.TryGetValue(tshKey, out var tshDict) || tshDict == null)
        {
            tshDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            options.AdditionalSections[tshKey] = tshDict;
        }

        // Synchronize settings if they already exist in tshDict to prevent desync/overwrites
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey))
            tshDict[GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey] = GameTimeFontSize.ToString(CultureInfo.InvariantCulture);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey))
            tshDict[GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey] = BoolToString(DrawScrollAnchor);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey))
            tshDict[GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey] = BoolToString(MoveScrollAnchor);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.LanguageFilterKey))
            tshDict[GameSettingsTheSuperHackersConstants.LanguageFilterKey] = BoolToString(LanguageFilter);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.SendDelayKey))
            tshDict[GameSettingsTheSuperHackersConstants.SendDelayKey] = BoolToString(SendDelay);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.ScrollFactorKey))
            tshDict[GameSettingsTheSuperHackersConstants.ScrollFactorKey] = ScrollFactor.ToString(CultureInfo.InvariantCulture);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.UseDoubleClickAttackMoveKey))
            tshDict[GameSettingsTheSuperHackersConstants.UseDoubleClickAttackMoveKey] = BoolToString(UseDoubleClickAttackMove);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.RetaliationKey))
            tshDict[GameSettingsTheSuperHackersConstants.RetaliationKey] = BoolToString(Retaliation);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.DynamicLODKey))
            tshDict[GameSettingsTheSuperHackersConstants.DynamicLODKey] = BoolToString(DynamicLOD);
        if (tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.MaxParticleCountKey))
            tshDict[GameSettingsTheSuperHackersConstants.MaxParticleCountKey] = MaxParticleCount.ToString(CultureInfo.InvariantCulture);

        // Update only the remaining settings we know about in the ViewModel, preserve all others
        tshDict[GameSettingsTheSuperHackersConstants.ArchiveReplaysKey] = BoolToString(TshArchiveReplays);
        tshDict[GameSettingsTheSuperHackersConstants.ShowMoneyPerMinuteKey] = BoolToString(TshShowMoneyPerMinute);
        tshDict[GameSettingsTheSuperHackersConstants.PlayerObserverEnabledKey] = BoolToString(TshPlayerObserverEnabled);
        tshDict[GameSettingsTheSuperHackersConstants.SystemTimeFontSizeKey] = TshSystemTimeFontSize.ToString(CultureInfo.InvariantCulture);
        tshDict[GameSettingsTheSuperHackersConstants.NetworkLatencyFontSizeKey] = TshNetworkLatencyFontSize.ToString(CultureInfo.InvariantCulture);
        tshDict[GameSettingsTheSuperHackersConstants.RenderFpsFontSizeKey] = TshRenderFpsFontSize.ToString(CultureInfo.InvariantCulture);
        tshDict[GameSettingsTheSuperHackersConstants.ResolutionFontAdjustmentKey] = TshResolutionFontAdjustment.ToString(CultureInfo.InvariantCulture);
        tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenGameKey] = BoolToString(TshCursorCaptureEnabledInFullscreenGame);
        tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenMenuKey] = BoolToString(TshCursorCaptureEnabledInFullscreenMenu);
        tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedGameKey] = BoolToString(TshCursorCaptureEnabledInWindowedGame);
        tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedMenuKey] = BoolToString(TshCursorCaptureEnabledInWindowedMenu);
        tshDict[GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInFullscreenAppKey] = BoolToString(TshScreenEdgeScrollEnabledInFullscreenApp);
        tshDict[GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInWindowedAppKey] = BoolToString(TshScreenEdgeScrollEnabledInWindowedApp);
        tshDict[GameSettingsTheSuperHackersConstants.MoneyTransactionVolumeKey] = TshMoneyTransactionVolume.ToString(CultureInfo.InvariantCulture);
        tshDict[GameSettingsTheSuperHackersConstants.GameWindowTransitionSpeedMultiplierKey] = (GameSettingsMapper.NormalizeTransitionSpeedMultiplier(TshGameWindowTransitionSpeedMultiplier) ?? GameSettingsTheSuperHackersConstants.DefaultGameWindowTransitionSpeedMultiplier).ToString(CultureInfo.InvariantCulture);

        return options;
    }

    private void ApplyGeneralsOnlineSettings(GeneralsOnlineSettings settings)
    {
        settings.EnsureNestedSectionsInitialized();

        GoShowFps = settings.ShowFps;
        GoShowPing = settings.ShowPing;
        GoShowPlayerRanks = settings.ShowPlayerRanks;
        GoAutoLogin = settings.AutoLogin;
        GoRememberUsername = settings.RememberUsername;
        GoEnableNotifications = settings.EnableNotifications;
        GoEnableSoundNotifications = settings.EnableSoundNotifications;
        GoChatFontSize = Math.Clamp(settings.ChatFontSize, MinGoChatFontSize, MaxGoChatFontSize);
        GoCameraMaxHeightOnlyWhenLobbyHost = settings.Camera.MaxHeightOnlyWhenLobbyHost;
        GoCameraMinHeight = settings.Camera.MinHeight;
        GoCameraMoveSpeedRatio = settings.Camera.MoveSpeedRatio;
        GoChatDurationSecondsUntilFadeOut = settings.Chat.DurationSecondsUntilFadeOut;
        GoDebugVerboseLogging = settings.Debug.VerboseLogging;
        GoRenderFpsLimit = settings.Render.FpsLimit;
        GoRenderLimitFramerate = settings.Render.LimitFramerate;
        GoRenderStatsOverlay = settings.Render.StatsOverlay;
        GoSocialNotificationFriendComesOnlineGameplay = settings.Social.NotificationFriendComesOnlineGameplay;
        GoSocialNotificationFriendComesOnlineMenus = settings.Social.NotificationFriendComesOnlineMenus;
        GoSocialNotificationFriendGoesOfflineGameplay = settings.Social.NotificationFriendGoesOfflineGameplay;
        GoSocialNotificationFriendGoesOfflineMenus = settings.Social.NotificationFriendGoesOfflineMenus;
        GoSocialNotificationPlayerAcceptsRequestGameplay = settings.Social.NotificationPlayerAcceptsRequestGameplay;
        GoSocialNotificationPlayerAcceptsRequestMenus = settings.Social.NotificationPlayerAcceptsRequestMenus;
        GoSocialNotificationPlayerSendsRequestGameplay = settings.Social.NotificationPlayerSendsRequestGameplay;
        GoSocialNotificationPlayerSendsRequestMenus = settings.Social.NotificationPlayerSendsRequestMenus;
    }

    /// <summary>
    /// Decides whether this save may rewrite settings.json, which is a single global file owned by
    /// the GeneralsOnline client rather than a per-profile one. Saving a retail, TheSuperHackers or
    /// CommunityOutpost profile must leave it untouched.
    /// </summary>
    /// <returns>True when the profile being edited runs the GeneralsOnline client.</returns>
    private bool ShouldWriteGeneralsOnlineSettings()
    {
        return SelectedGameType == GameType.ZeroHour && _currentProfileIsGeneralsOnline;
    }

    /// <summary>
    /// Writes this view model's GeneralsOnline values into settings just read from the client's
    /// settings.json, which is what carries the keys this model does not declare through a save.
    /// </summary>
    /// <param name="settings">The settings read from settings.json, mutated in place.</param>
    private void MergeViewModelIntoGeneralsOnlineSettings(GeneralsOnlineSettings settings)
    {
        settings.EnsureNestedSectionsInitialized();

        settings.ShowFps = GoShowFps;
        settings.ShowPing = GoShowPing;
        settings.ShowPlayerRanks = GoShowPlayerRanks;
        settings.AutoLogin = GoAutoLogin;
        settings.RememberUsername = GoRememberUsername;
        settings.EnableNotifications = GoEnableNotifications;
        settings.EnableSoundNotifications = GoEnableSoundNotifications;
        settings.ChatFontSize = Math.Clamp(GoChatFontSize, MinGoChatFontSize, MaxGoChatFontSize);
        settings.Camera.MaxHeightOnlyWhenLobbyHost = GoCameraMaxHeightOnlyWhenLobbyHost;
        settings.Camera.MinHeight = GoCameraMinHeight;
        settings.Camera.MoveSpeedRatio = GoCameraMoveSpeedRatio;
        settings.Chat.DurationSecondsUntilFadeOut = GoChatDurationSecondsUntilFadeOut;
        settings.Debug.VerboseLogging = GoDebugVerboseLogging;
        settings.Render.FpsLimit = GoRenderFpsLimit;
        settings.Render.LimitFramerate = GoRenderLimitFramerate;
        settings.Render.StatsOverlay = GoRenderStatsOverlay;
        settings.Social.NotificationFriendComesOnlineGameplay = GoSocialNotificationFriendComesOnlineGameplay;
        settings.Social.NotificationFriendComesOnlineMenus = GoSocialNotificationFriendComesOnlineMenus;
        settings.Social.NotificationFriendGoesOfflineGameplay = GoSocialNotificationFriendGoesOfflineGameplay;
        settings.Social.NotificationFriendGoesOfflineMenus = GoSocialNotificationFriendGoesOfflineMenus;
        settings.Social.NotificationPlayerAcceptsRequestGameplay = GoSocialNotificationPlayerAcceptsRequestGameplay;
        settings.Social.NotificationPlayerAcceptsRequestMenus = GoSocialNotificationPlayerAcceptsRequestMenus;
        settings.Social.NotificationPlayerSendsRequestGameplay = GoSocialNotificationPlayerSendsRequestGameplay;
        settings.Social.NotificationPlayerSendsRequestMenus = GoSocialNotificationPlayerSendsRequestMenus;
    }

    private string GetWarningTitle() =>
        localizationService?.GetString(WarningTitleKey) ?? DefaultWarningTitle;

    private string GetServiceUnavailableMessage() =>
        localizationService?.GetString(ServiceUnavailableMessageKey) ?? DefaultServiceUnavailableMessage;

    private string GetUnknownGameTypeMessage() =>
        localizationService?.GetString(UnknownGameTypeMessageKey) ?? DefaultUnknownGameTypeMessage;

    private string GetErrorLoadingSettingsTitle() =>
        localizationService?.GetString(ErrorLoadingSettingsTitleKey) ?? DefaultErrorLoadingSettingsTitle;

    private string GetErrorSavingSettingsTitle() =>
        localizationService?.GetString(ErrorSavingSettingsTitleKey) ?? DefaultErrorSavingSettingsTitle;

    private string GetErrorOpeningLocationTitle() =>
        localizationService?.GetString(ErrorOpeningLocationTitleKey) ?? DefaultErrorOpeningLocationTitle;

    private string GetErrorOpeningLocationMessage() =>
        localizationService?.GetString(ErrorOpeningLocationMessageKey) ?? DefaultErrorOpeningLocationMessage;

    private string GetSettingsSavedTitle() =>
        localizationService?.GetString(SettingsSavedTitleKey) ?? DefaultSettingsSavedTitle;

    private string GetSettingsSavedMessage(GameType gameType)
    {
        var format = localizationService?.GetString(SettingsSavedMessageKey) ?? DefaultSettingsSavedMessage;
        return string.Format(CultureInfo.CurrentCulture, format, gameType);
    }

    private string GetDefaultSettingsLoadedTitle() =>
        localizationService?.GetString(DefaultSettingsLoadedTitleKey) ?? DefaultDefaultSettingsLoadedTitle;

    private string GetDefaultSettingsLoadedMessage() =>
        localizationService?.GetString(DefaultSettingsLoadedMessageKey) ?? DefaultDefaultSettingsLoadedMessage;

    private string GetResolutionSetTitle() =>
        localizationService?.GetString(ResolutionSetTitleKey) ?? DefaultResolutionSetTitle;

    private string GetResolutionSetMessage(int width, int height)
    {
        var format = localizationService?.GetString(ResolutionSetMessageKey) ?? DefaultResolutionSetMessage;
        return string.Format(CultureInfo.CurrentCulture, format, width, height);
    }

    private string GetInvalidResolutionPresetMessage(string? preset)
    {
        var format = localizationService?.GetString(InvalidResolutionPresetMessageKey) ?? DefaultInvalidResolutionPresetMessage;
        return string.Format(CultureInfo.CurrentCulture, format, preset);
    }
}
