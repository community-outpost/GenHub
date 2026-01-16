using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the Game Settings tab in Profile Settings.
/// Manages Options.ini for Generals and Zero Hour.
/// </summary>
public partial class GameSettingsViewModel(IGameSettingsService gameSettingsService, ILogger<GameSettingsViewModel> logger) : ViewModelBase
{
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

    private readonly IGameSettingsService? _gameSettingsService = gameSettingsService;
    private readonly ILogger<GameSettingsViewModel> _logger = logger;

    [ObservableProperty]
    private GameType _selectedGameType;

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
    private bool _tshPlayerObserverEnabled;

    [ObservableProperty]
    private int _tshSystemTimeFontSize = 12;

    [ObservableProperty]
    private int _tshNetworkLatencyFontSize = 12;

    [ObservableProperty]
    private int _tshRenderFpsFontSize = 12;

    [ObservableProperty]
    private int _tshResolutionFontAdjustment = -100;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInFullscreenGame;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInFullscreenMenu;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInWindowedGame;

    [ObservableProperty]
    private bool _tshCursorCaptureEnabledInWindowedMenu;

    [ObservableProperty]
    private bool _tshScreenEdgeScrollEnabledInFullscreenApp;

    [ObservableProperty]
    private bool _tshScreenEdgeScrollEnabledInWindowedApp;

    [ObservableProperty]
    private int _tshMoneyTransactionVolume = 50;

    // ===== GeneralsOnline Client Settings =====
    [ObservableProperty]
    private bool _goShowFps;

    [ObservableProperty]
    private bool _goShowPing;

    [ObservableProperty]
    private bool _goShowPlayerRanks;

    [ObservableProperty]
    private bool _goAutoLogin;

    [ObservableProperty]
    private bool _goRememberUsername;

    [ObservableProperty]
    private bool _goEnableNotifications;

    [ObservableProperty]
    private bool _goEnableSoundNotifications;

    [ObservableProperty]
    private int _goChatFontSize = 12;

    // Camera settings
    [ObservableProperty]
    private float _goCameraMaxHeightOnlyWhenLobbyHost = 310.0f;

    [ObservableProperty]
    private float _goCameraMinHeight = 310.0f;

    [ObservableProperty]
    private float _goCameraMoveSpeedRatio = 1.5f;

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
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeForProfileAsync(string? profileId, Core.Models.GameProfile.GameProfile? profile = null)
    {
        _initializationDepth++;
        IsLoading = true;  // Provide UI feedback for loading state

        try
        {
            _currentProfileId = profileId;

            // Auto-select game type from profile
            if (profile != null)
            {
                if (profile.IsToolProfile)
                {
                    StatusMessage = ProfileValidationConstants.ToolProfileSettingsNotApplicable;
                    _logger.LogInformation("Skipping settings load for Tool profile {ProfileId}", profileId);
                    return;
                }

                SelectedGameType = profile.GameClient.GameType;
                _logger.LogInformation(
                    "Auto-selected game type {GameType} for profile {ProfileId}",
                    SelectedGameType,
                    profileId);
            }
            else
            {
                // Ensure we log what we're doing
                _logger.LogInformation("Using pre-selected GameType {GameType} for new profile initialization", SelectedGameType);
            }

            // If profile has settings, load them
            if (profile != null && profile.HasCustomSettings())
            {
                LoadSettingsFromProfile(profile);
            }
            else
            {
                // For new profiles or profiles without settings, load from Options.ini as defaults
                _isLoadingFromOptions = true;
                await LoadSettingsCommand.ExecuteAsync(null);
                _isLoadingFromOptions = false;
                StatusMessage = "Loaded default settings from Options.ini. Save the profile to persist these settings.";
            }
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

            // GeneralsOnline settings
            GoShowFps = GoShowFps,
            GoShowPing = GoShowPing,
            GoShowPlayerRanks = GoShowPlayerRanks,
            GoAutoLogin = GoAutoLogin,
            GoRememberUsername = GoRememberUsername,
            GoEnableNotifications = GoEnableNotifications,
            GoEnableSoundNotifications = GoEnableSoundNotifications,
            GoChatFontSize = GoChatFontSize,

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
            _logger.LogWarning("Failed to parse resolution preset: {Preset}", preset);
            return;
        }

        ResolutionWidth = width;
        ResolutionHeight = height;
        StatusMessage = $"Resolution set to {width}x{height}";
    }

    private IniOptions? _currentOptions;
    private string? _currentProfileId;
    private int _initializationDepth;
    private bool _isLoadingFromOptions;

    /// <summary>
    /// Loads the options.ini settings for the selected game type.
    /// </summary>
    [RelayCommand]
    private async Task LoadSettings()
    {
        if (_gameSettingsService == null)
        {
            StatusMessage = "Game settings service not available";
            return;
        }

        GameType gameType = SelectedGameType;
        try
        {
            IsLoading = true;

            StatusMessage = $"Loading {gameType} settings...";

            OptionsFilePath = _gameSettingsService.GetOptionsFilePath(gameType);
            OptionsFileExists = _gameSettingsService.OptionsFileExists(gameType);

            var result = await _gameSettingsService.LoadOptionsAsync(gameType);

            if (result?.Success == true && result.Data != null)
            {
                _currentOptions = result.Data;
                ApplyOptionsToViewModel(_currentOptions);

                StatusMessage = OptionsFileExists
                    ? $"Loaded {gameType} settings from {Path.GetFileName(OptionsFilePath)}"
                    : $"Using default {gameType} settings (file not found)";

                _logger.LogInformation("Loaded settings for {GameType}", gameType);
            }
            else
            {
                var errors = result?.Errors ?? ["LoadOptions result was null"];
                StatusMessage = $"Failed to load settings: {string.Join(", ", errors)}";
                _logger.LogWarning("Failed to load settings for {GameType}: {Errors}", gameType, string.Join(", ", errors));
            }

            // Load GeneralsOnline settings separately
            var goResult = await _gameSettingsService.LoadGeneralsOnlineSettingsAsync();
            if (goResult?.Success == true && goResult.Data != null)
            {
                ApplyGeneralsOnlineSettings(goResult.Data);
                _logger.LogInformation("Loaded GeneralsOnline settings");
            }
            else
            {
                var goErrors = goResult?.Errors ?? ["LoadGeneralsOnlineSettings result was null"];
                _logger.LogWarning("Failed to load GeneralsOnline settings: {Errors}", string.Join(", ", goErrors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings for {GameType}", gameType);
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads settings from a game profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    private void LoadSettingsFromProfile(Core.Models.GameProfile.GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _logger.LogInformation("Loading settings from profile {ProfileId}", _currentProfileId);

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

        if (profile.AudioSoundVolume.HasValue) SoundVolume = profile.AudioSoundVolume.Value;
        if (profile.AudioThreeDSoundVolume.HasValue) ThreeDSoundVolume = profile.AudioThreeDSoundVolume.Value;
        if (profile.AudioSpeechVolume.HasValue) SpeechVolume = profile.AudioSpeechVolume.Value;
        if (profile.AudioMusicVolume.HasValue) MusicVolume = profile.AudioMusicVolume.Value;
        if (profile.AudioEnabled.HasValue) AudioEnabled = profile.AudioEnabled.Value;
        if (profile.AudioNumSounds.HasValue) NumSounds = profile.AudioNumSounds.Value;

        // TheSuperHackers settings
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

        // GeneralsOnline settings
        if (profile.GoShowFps.HasValue) GoShowFps = profile.GoShowFps.Value;
        if (profile.GoShowPing.HasValue) GoShowPing = profile.GoShowPing.Value;
        if (profile.GoShowPlayerRanks.HasValue) GoShowPlayerRanks = profile.GoShowPlayerRanks.Value;
        if (profile.GoAutoLogin.HasValue) GoAutoLogin = profile.GoAutoLogin.Value;
        if (profile.GoRememberUsername.HasValue) GoRememberUsername = profile.GoRememberUsername.Value;
        if (profile.GoEnableNotifications.HasValue) GoEnableNotifications = profile.GoEnableNotifications.Value;
        if (profile.GoEnableSoundNotifications.HasValue) GoEnableSoundNotifications = profile.GoEnableSoundNotifications.Value;
        if (profile.GoChatFontSize.HasValue) GoChatFontSize = profile.GoChatFontSize.Value;

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

        if (profile.GameSpyIPAddress != null) GameSpyIPAddress = profile.GameSpyIPAddress;

        // Update selected preset if it matches
        var currentRes = $"{ResolutionWidth}x{ResolutionHeight}";
        SelectedResolutionPreset = ResolutionPresets.Contains(currentRes) ? currentRes : null;

        StatusMessage = $"Loaded profile settings for {profile.GameClient.GameType}";
        _logger.LogInformation(
            "Loaded profile settings - Windowed={Windowed}, Resolution={Width}x{Height}",
            Windowed,
            ResolutionWidth,
            ResolutionHeight);
    }

    /// <summary>
    /// Saves the current settings to options.ini.
    /// </summary>
    [RelayCommand]
    private async Task SaveSettings()
    {
        if (_gameSettingsService == null)
        {
            StatusMessage = "Game settings service not available";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Saving {SelectedGameType} settings...";

            var options = CreateOptionsFromViewModel();
            var result = await _gameSettingsService.SaveOptionsAsync(SelectedGameType, options);

            // Save GeneralsOnline settings
            var goSettings = CreateGeneralsOnlineSettings();
            var goResult = await _gameSettingsService.SaveGeneralsOnlineSettingsAsync(goSettings);

            if (result?.Success == true && goResult?.Success == true)
            {
                _currentOptions = options;
                OptionsFileExists = true;
                StatusMessage = $"{SelectedGameType} settings saved successfully";
                _logger.LogInformation("Saved settings for {GameType}", SelectedGameType);
            }
            else
            {
                var errors = new List<string>();
                if (result?.Success == false) errors.AddRange(result.Errors);
                if (goResult?.Success == false) errors.AddRange(goResult.Errors);
                if (result == null) errors.Add("SaveOptions result was null");
                if (goResult == null) errors.Add("SaveGeneralsOnlineSettings result was null");

                StatusMessage = $"Failed to save settings: {string.Join(", ", errors)}";
                _logger.LogWarning("Failed to save settings: {Errors}", string.Join(", ", errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings for {GameType}", SelectedGameType);
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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
                System.Diagnostics.Process.Start("explorer.exe", directory);
                _logger.LogInformation("Opened file location {Directory}", directory);
            }
            else
            {
                StatusMessage = "Options file directory not found";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening file location");
            StatusMessage = $"Error opening location: {ex.Message}";
        }
    }

    partial void OnSelectedResolutionPresetChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ApplyResolutionPreset(value);
        }
    }

    partial void OnSelectedGameTypeChanged(GameType value)
    {
        if (_initializationDepth == 0 && !_isLoadingFromOptions)
        {
            _logger.LogInformation("GameType changed to {GameType} - loading from Options.ini", value);
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadSettingsCommand.ExecuteAsync(null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load settings for {GameType}", value);
                    StatusMessage = $"Error loading settings: {ex.Message}";
                }
            });
        }
        else if (_isLoadingFromOptions)
        {
            _logger.LogInformation("GameType changed to {GameType} while loading from Options.ini - skipping auto-load", value);
        }
        else
        {
            _logger.LogInformation("GameType set to {GameType} during initialization - skipping auto-load", value);
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

        _logger.LogInformation(
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

        // Load GenHub custom properties
        if (options.Video.AdditionalProperties.TryGetValue("GenHubParticleEffects", out var particleEffects))
            ParticleEffects = ParseBool(particleEffects);
        if (options.Video.AdditionalProperties.TryGetValue("GenHubBuildingAnimations", out var buildingAnimations))
            BuildingAnimations = ParseBool(buildingAnimations);

        // Load additional video settings
        if (options.Video.AdditionalProperties.TryGetValue("StaticGameLOD", out var staticLOD))
            StaticGameLOD = staticLOD;
        if (options.Video.AdditionalProperties.TryGetValue("IdealStaticGameLOD", out var idealLOD))
            IdealStaticGameLOD = idealLOD;

        // Graphics Settings
        if (options.Video.AdditionalProperties.TryGetValue("ShowSoftWaterEdge", out var swe)) ShowSoftWaterEdge = ParseBool(swe);
        if (options.Video.AdditionalProperties.TryGetValue("ShowTrees", out var st)) ShowTrees = ParseBool(st);
        if (options.Video.AdditionalProperties.TryGetValue("UseCloudMap", out var ucm)) UseCloudMap = ParseBool(ucm);
        if (options.Video.AdditionalProperties.TryGetValue("UseLightMap", out var ulm)) UseLightMap = ParseBool(ulm);

        // Control/Misc Settings
        if (options.Video.AdditionalProperties.TryGetValue("DrawScrollAnchor", out var draws)) DrawScrollAnchor = ParseBool(draws);
        if (options.Video.AdditionalProperties.TryGetValue("MoveScrollAnchor", out var moves)) MoveScrollAnchor = ParseBool(moves);
        if (options.Video.AdditionalProperties.TryGetValue("GameTimeFontSize", out var gtfs) && int.TryParse(gtfs, out var gtfsVal)) GameTimeFontSize = gtfsVal;
        if (options.Video.AdditionalProperties.TryGetValue("LanguageFilter", out var lf)) LanguageFilter = ParseBool(lf);
        if (options.Video.AdditionalProperties.TryGetValue("SendDelay", out var sd)) SendDelay = ParseBool(sd);
        if (options.Video.AdditionalProperties.TryGetValue("SkipEALogo", out var sel)) SkipEALogo = ParseBool(sel);

        AntiAliasing = options.Video.AntiAliasing;

        // Load TSH-specific settings from the [TheSuperHackers] section
        if (options.AdditionalSections.TryGetValue("TheSuperHackers", out var tsh))
        {
            if (tsh.TryGetValue("UseDoubleClickAttackMove", out var doubleClick))
                UseDoubleClickAttackMove = ParseBool(doubleClick);
            if (tsh.TryGetValue("ScrollFactor", out var scroll) && int.TryParse(scroll, out var scrollVal))
                ScrollFactor = scrollVal;
            if (tsh.TryGetValue("Retaliation", out var retaliation))
                Retaliation = ParseBool(retaliation);
            if (tsh.TryGetValue("DynamicLOD", out var dynLOD))
                DynamicLOD = ParseBool(dynLOD);
            if (tsh.TryGetValue("MaxParticleCount", out var particles) && int.TryParse(particles, out var particleVal))
                MaxParticleCount = particleVal;

            if (tsh.TryGetValue("ArchiveReplays", out var ar)) TshArchiveReplays = ParseBool(ar);
            if (tsh.TryGetValue("ShowMoneyPerMinute", out var smpm)) TshShowMoneyPerMinute = ParseBool(smpm);
            if (tsh.TryGetValue("PlayerObserverEnabled", out var poe)) TshPlayerObserverEnabled = ParseBool(poe);
            if (tsh.TryGetValue("SystemTimeFontSize", out var stfs) && int.TryParse(stfs, out var stfsVal)) TshSystemTimeFontSize = stfsVal;
            if (tsh.TryGetValue("NetworkLatencyFontSize", out var nlfs) && int.TryParse(nlfs, out var nlfsVal)) TshNetworkLatencyFontSize = nlfsVal;
            if (tsh.TryGetValue("RenderFpsFontSize", out var rffs) && int.TryParse(rffs, out var rffsVal)) TshRenderFpsFontSize = rffsVal;
            if (tsh.TryGetValue("ResolutionFontAdjustment", out var rfa) && int.TryParse(rfa, out var rfaVal)) TshResolutionFontAdjustment = rfaVal;
            if (tsh.TryGetValue("CursorCaptureEnabledInFullscreenGame", out var ccefg)) TshCursorCaptureEnabledInFullscreenGame = ParseBool(ccefg);
            if (tsh.TryGetValue("CursorCaptureEnabledInFullscreenMenu", out var ccefm)) TshCursorCaptureEnabledInFullscreenMenu = ParseBool(ccefm);
            if (tsh.TryGetValue("CursorCaptureEnabledInWindowedGame", out var ccewg)) TshCursorCaptureEnabledInWindowedGame = ParseBool(ccewg);
            if (tsh.TryGetValue("CursorCaptureEnabledInWindowedMenu", out var ccewm)) TshCursorCaptureEnabledInWindowedMenu = ParseBool(ccewm);
            if (tsh.TryGetValue("ScreenEdgeScrollEnabledInFullscreenApp", out var sesefa)) TshScreenEdgeScrollEnabledInFullscreenApp = ParseBool(sesefa);
            if (tsh.TryGetValue("ScreenEdgeScrollEnabledInWindowedApp", out var sesewa)) TshScreenEdgeScrollEnabledInWindowedApp = ParseBool(sesewa);
            if (tsh.TryGetValue("MoneyTransactionVolume", out var mtv) && int.TryParse(mtv, out var mtvVal)) TshMoneyTransactionVolume = mtvVal;
        }

        ExtraAnimations = options.Video.ExtraAnimations;
        Gamma = options.Video.Gamma;
        AlternateMouseSetup = options.Video.AlternateMouseSetup;
        HeatEffects = options.Video.HeatEffects;

        GameSpyIPAddress = options.Network.GameSpyIPAddress;

        // Update selected preset if it matches
        var currentRes = $"{ResolutionWidth}x{ResolutionHeight}";
        SelectedResolutionPreset = ResolutionPresets.Contains(currentRes) ? currentRes : null;
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
        options.Video.TextureReduction = TextureReductionOffset - (int)TextureQuality;
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
        options.Video.AdditionalProperties["ScrollFactor"] = ScrollFactor.ToString();
        options.Video.AdditionalProperties["Retaliation"] = BoolToString(Retaliation);
        options.Video.AdditionalProperties["DynamicLOD"] = BoolToString(DynamicLOD);
        options.Video.AdditionalProperties["MaxParticleCount"] = MaxParticleCount.ToString();
        options.Video.AdditionalProperties["DrawScrollAnchor"] = BoolToString(DrawScrollAnchor);
        options.Video.AdditionalProperties["MoveScrollAnchor"] = BoolToString(MoveScrollAnchor);
        options.Video.AdditionalProperties["GameTimeFontSize"] = GameTimeFontSize.ToString();
        options.Video.AdditionalProperties["LanguageFilter"] = BoolToString(LanguageFilter);
        options.Video.AdditionalProperties["SendDelay"] = BoolToString(SendDelay);

        options.Video.ExtraAnimations = ExtraAnimations;
        options.Video.Gamma = Gamma;
        options.Video.AlternateMouseSetup = AlternateMouseSetup;
        options.Video.HeatEffects = HeatEffects;

        // Mirror keys for some TSH client versions
        options.Video.AdditionalProperties["UseAlternateMouse"] = BoolToString(AlternateMouseSetup);
        options.Video.AdditionalProperties["UseDoubleClick"] = BoolToString(UseDoubleClickAttackMove);

        options.Network.GameSpyIPAddress = GameSpyIPAddress;

        // TheSuperHackers settings - preserve existing settings, only update the ones we manage
        if (!options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshDict))
        {
            tshDict = [];
            options.AdditionalSections["TheSuperHackers"] = tshDict;
        }

        // Update only the remaining settings we know about in the ViewModel, preserve all others
        tshDict["ArchiveReplays"] = BoolToString(TshArchiveReplays);
        tshDict["ShowMoneyPerMinute"] = BoolToString(TshShowMoneyPerMinute);
        tshDict["PlayerObserverEnabled"] = BoolToString(TshPlayerObserverEnabled);
        tshDict["SystemTimeFontSize"] = TshSystemTimeFontSize.ToString();
        tshDict["NetworkLatencyFontSize"] = TshNetworkLatencyFontSize.ToString();
        tshDict["RenderFpsFontSize"] = TshRenderFpsFontSize.ToString();
        tshDict["ResolutionFontAdjustment"] = TshResolutionFontAdjustment.ToString();
        tshDict["CursorCaptureEnabledInFullscreenGame"] = BoolToString(TshCursorCaptureEnabledInFullscreenGame);
        tshDict["CursorCaptureEnabledInFullscreenMenu"] = BoolToString(TshCursorCaptureEnabledInFullscreenMenu);
        tshDict["CursorCaptureEnabledInWindowedGame"] = BoolToString(TshCursorCaptureEnabledInWindowedGame);
        tshDict["CursorCaptureEnabledInWindowedMenu"] = BoolToString(TshCursorCaptureEnabledInWindowedMenu);
        tshDict["ScreenEdgeScrollEnabledInFullscreenApp"] = BoolToString(TshScreenEdgeScrollEnabledInFullscreenApp);
        tshDict["ScreenEdgeScrollEnabledInWindowedApp"] = BoolToString(TshScreenEdgeScrollEnabledInWindowedApp);
        tshDict["MoneyTransactionVolume"] = TshMoneyTransactionVolume.ToString();

        return options;
    }

    private void ApplyGeneralsOnlineSettings(GeneralsOnlineSettings settings)
    {
        GoShowFps = settings.ShowFps;
        GoShowPing = settings.ShowPing;
        GoShowPlayerRanks = settings.ShowPlayerRanks;
        GoAutoLogin = settings.AutoLogin;
        GoRememberUsername = settings.RememberUsername;
        GoEnableNotifications = settings.EnableNotifications;
        GoEnableSoundNotifications = settings.EnableSoundNotifications;
        GoChatFontSize = settings.ChatFontSize;
        GoCameraMaxHeightOnlyWhenLobbyHost = settings.CameraMaxHeightOnlyWhenLobbyHost;
        GoCameraMinHeight = settings.CameraMinHeight;
        GoCameraMoveSpeedRatio = settings.CameraMoveSpeedRatio;
        GoChatDurationSecondsUntilFadeOut = settings.ChatDurationSecondsUntilFadeOut;
        GoDebugVerboseLogging = settings.DebugVerboseLogging;
        GoRenderFpsLimit = settings.RenderFpsLimit;
        GoRenderLimitFramerate = settings.RenderLimitFramerate;
        GoRenderStatsOverlay = settings.RenderStatsOverlay;
        GoSocialNotificationFriendComesOnlineGameplay = settings.SocialNotificationFriendComesOnlineGameplay;
        GoSocialNotificationFriendComesOnlineMenus = settings.SocialNotificationFriendComesOnlineMenus;
        GoSocialNotificationFriendGoesOfflineGameplay = settings.SocialNotificationFriendGoesOfflineGameplay;
        GoSocialNotificationFriendGoesOfflineMenus = settings.SocialNotificationFriendGoesOfflineMenus;
        GoSocialNotificationPlayerAcceptsRequestGameplay = settings.SocialNotificationPlayerAcceptsRequestGameplay;
        GoSocialNotificationPlayerAcceptsRequestMenus = settings.SocialNotificationPlayerAcceptsRequestMenus;
        GoSocialNotificationPlayerSendsRequestGameplay = settings.SocialNotificationPlayerSendsRequestGameplay;
        GoSocialNotificationPlayerSendsRequestMenus = settings.SocialNotificationPlayerSendsRequestMenus;
    }

    private GeneralsOnlineSettings CreateGeneralsOnlineSettings()
    {
        return new GeneralsOnlineSettings
        {
            ShowFps = GoShowFps,
            ShowPing = GoShowPing,
            ShowPlayerRanks = GoShowPlayerRanks,
            AutoLogin = GoAutoLogin,
            RememberUsername = GoRememberUsername,
            EnableNotifications = GoEnableNotifications,
            EnableSoundNotifications = GoEnableSoundNotifications,
            ChatFontSize = GoChatFontSize,
            CameraMaxHeightOnlyWhenLobbyHost = GoCameraMaxHeightOnlyWhenLobbyHost,
            CameraMinHeight = GoCameraMinHeight,
            CameraMoveSpeedRatio = GoCameraMoveSpeedRatio,
            ChatDurationSecondsUntilFadeOut = GoChatDurationSecondsUntilFadeOut,
            DebugVerboseLogging = GoDebugVerboseLogging,
            RenderFpsLimit = GoRenderFpsLimit,
            RenderLimitFramerate = GoRenderLimitFramerate,
            RenderStatsOverlay = GoRenderStatsOverlay,
            SocialNotificationFriendComesOnlineGameplay = GoSocialNotificationFriendComesOnlineGameplay,
            SocialNotificationFriendComesOnlineMenus = GoSocialNotificationFriendComesOnlineMenus,
            SocialNotificationFriendGoesOfflineGameplay = GoSocialNotificationFriendGoesOfflineGameplay,
            SocialNotificationFriendGoesOfflineMenus = GoSocialNotificationFriendGoesOfflineMenus,
            SocialNotificationPlayerAcceptsRequestGameplay = GoSocialNotificationPlayerAcceptsRequestGameplay,
            SocialNotificationPlayerAcceptsRequestMenus = GoSocialNotificationPlayerAcceptsRequestMenus,
            SocialNotificationPlayerSendsRequestGameplay = GoSocialNotificationPlayerSendsRequestGameplay,
            SocialNotificationPlayerSendsRequestMenus = GoSocialNotificationPlayerSendsRequestMenus,
        };
    }
}
