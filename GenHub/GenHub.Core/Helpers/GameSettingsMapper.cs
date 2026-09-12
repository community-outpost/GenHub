using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper class for mapping between game profiles and INI options.
/// </summary>
public static class GameSettingsMapper
{
    /// <summary>
    /// Applies settings from IniOptions to a GameProfile.
    /// Used when creating new profiles to inherit existing game settings.
    /// </summary>
    /// <param name="options">The IniOptions containing the settings.</param>
    /// <param name="profile">The GameProfile to populate.</param>
    public static void ApplyFromOptions(IniOptions options, GameProfile profile)
    {
        ApplyVideoFromOptions(options, profile);
        ApplyAudioFromOptions(options, profile);
        ApplyNetworkFromOptions(options, profile);
    }

    /// <summary>
    /// Applies settings from GeneralsOnlineSettings to a GameProfile.
    /// Used when creating new profiles to inherit existing GO settings.
    /// </summary>
    /// <param name="settings">The GeneralsOnlineSettings source.</param>
    /// <param name="profile">The GameProfile to populate.</param>
    public static void ApplyFromGeneralsOnlineSettings(GeneralsOnlineSettings settings, GameProfile profile)
    {
        // GeneralsOnline settings
        profile.GoShowFps = settings.ShowFps;
        profile.GoShowPing = settings.ShowPing;
        profile.GoShowPlayerRanks = settings.ShowPlayerRanks;
        profile.GoAutoLogin = settings.AutoLogin;
        profile.GoRememberUsername = settings.RememberUsername;
        profile.GoEnableNotifications = settings.EnableNotifications;
        profile.GoEnableSoundNotifications = settings.EnableSoundNotifications;
        profile.GoChatFontSize = settings.ChatFontSize;

        // Camera settings
        profile.GoCameraMaxHeightOnlyWhenLobbyHost = settings.Camera.MaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = settings.Camera.MinHeight;
        profile.GoCameraMoveSpeedRatio = settings.Camera.MoveSpeedRatio;

        // Chat settings
        profile.GoChatDurationSecondsUntilFadeOut = settings.Chat.DurationSecondsUntilFadeOut;

        // Debug settings
        profile.GoDebugVerboseLogging = settings.Debug.VerboseLogging;

        // Render settings
        profile.GoRenderFpsLimit = settings.Render.FpsLimit;
        profile.GoRenderLimitFramerate = settings.Render.LimitFramerate;
        profile.GoRenderStatsOverlay = settings.Render.StatsOverlay;

        // Social notification settings
        profile.GoSocialNotificationFriendComesOnlineGameplay = settings.Social.NotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = settings.Social.NotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = settings.Social.NotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = settings.Social.NotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = settings.Social.NotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = settings.Social.NotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = settings.Social.NotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = settings.Social.NotificationPlayerSendsRequestMenus;
    }

    /// <summary>
    /// Applies settings from a GameProfile to a GeneralsOnlineSettings object.
    /// Used by GameLauncher to prepare settings.json for launch.
    /// </summary>
    /// <remarks>
    /// Only the fields the profile declares are written, as <see cref="ApplyToOptions"/> does for
    /// Options.ini. The caller passes the settings already on disk, and anything the profile leaves
    /// unset is the GeneralsOnline client's own configuration, which a launch must not overwrite.
    /// </remarks>
    /// <param name="profile">The GameProfile source.</param>
    /// <param name="settings">The GeneralsOnlineSettings to populate.</param>
    public static void ApplyToGeneralsOnlineSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        settings.EnsureNestedSectionsInitialized();

        ApplyGoGeneralSettings(profile, settings);
        ApplyGoCameraAndChatSettings(profile, settings);
        ApplyGoRenderAndDebugSettings(profile, settings);
        ApplyGoSocialSettings(profile, settings);
    }

    /// <summary>
    /// Applies profile settings to IniOptions with validation.
    /// </summary>
    /// <param name="profile">The game profile containing the settings.</param>
    /// <param name="options">The IniOptions object to apply settings to.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    public static void ApplyToOptions(GameProfile profile, IniOptions options, ILogger? logger = null)
    {
        ApplyVideoResolutionAndQualityToOptions(profile, options, logger);
        ApplyVideoAdditionalToOptions(profile, options, logger);
        ApplyAudioToOptions(profile, options, logger);
        ApplyTshToOptions(profile, options);
    }

    /// <summary>
    /// Populates settings from a CreateProfileRequest into a GameProfile.
    /// </summary>
    /// <param name="profile">The GameProfile to populate.</param>
    /// <param name="request">The request containing the settings.</param>
    public static void PopulateGameProfile(GameProfile profile, CreateProfileRequest request)
    {
        // Video settings
        profile.VideoResolutionWidth = request.VideoResolutionWidth;
        profile.VideoResolutionHeight = request.VideoResolutionHeight;
        profile.VideoWindowed = request.VideoWindowed;
        profile.VideoTextureQuality = request.VideoTextureQuality;
        profile.EnableVideoShadows = request.EnableVideoShadows;
        profile.VideoParticleEffects = request.VideoParticleEffects;
        profile.VideoExtraAnimations = request.VideoExtraAnimations;
        profile.VideoBuildingAnimations = request.VideoBuildingAnimations;
        profile.VideoGamma = request.VideoGamma;
        profile.VideoAlternateMouseSetup = request.VideoAlternateMouseSetup;
        profile.VideoHeatEffects = request.VideoHeatEffects;
        profile.VideoStaticGameLOD = request.VideoStaticGameLOD;
        profile.VideoIdealStaticGameLOD = request.VideoIdealStaticGameLOD;
        profile.VideoUseDoubleClickAttackMove = request.VideoUseDoubleClickAttackMove;
        profile.VideoScrollFactor = request.VideoScrollFactor;
        profile.VideoRetaliation = request.VideoRetaliation;
        profile.VideoDynamicLOD = request.VideoDynamicLOD;
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount;
        profile.VideoAntiAliasing = request.VideoAntiAliasing;
        profile.VideoSkipEALogo = request.VideoSkipEALogo;
        profile.VideoDrawScrollAnchor = request.VideoDrawScrollAnchor;
        profile.VideoMoveScrollAnchor = request.VideoMoveScrollAnchor;
        profile.VideoGameTimeFontSize = request.VideoGameTimeFontSize;
        profile.GameLanguageFilter = request.GameLanguageFilter;
        profile.NetworkSendDelay = request.NetworkSendDelay;
        profile.VideoShowSoftWaterEdge = request.VideoShowSoftWaterEdge;
        profile.VideoShowTrees = request.VideoShowTrees;
        profile.VideoUseCloudMap = request.VideoUseCloudMap;
        profile.VideoUseLightMap = request.VideoUseLightMap;
        profile.VideoUseShadowDecals = request.VideoUseShadowDecals;
        profile.VideoBuildingOcclusion = request.VideoBuildingOcclusion;
        profile.VideoShowProps = request.VideoShowProps;

        // Audio settings
        profile.AudioSoundVolume = request.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds;

        // TheSuperHackers settings
        profile.TshArchiveReplays = request.TshArchiveReplays;
        profile.TshShowMoneyPerMinute = request.TshShowMoneyPerMinute;
        profile.TshPlayerObserverEnabled = request.TshPlayerObserverEnabled;
        profile.TshSystemTimeFontSize = request.TshSystemTimeFontSize;
        profile.TshNetworkLatencyFontSize = request.TshNetworkLatencyFontSize;
        profile.TshRenderFpsFontSize = request.TshRenderFpsFontSize;
        profile.TshResolutionFontAdjustment = request.TshResolutionFontAdjustment;
        profile.TshCursorCaptureEnabledInFullscreenGame = request.TshCursorCaptureEnabledInFullscreenGame;
        profile.TshCursorCaptureEnabledInFullscreenMenu = request.TshCursorCaptureEnabledInFullscreenMenu;
        profile.TshCursorCaptureEnabledInWindowedGame = request.TshCursorCaptureEnabledInWindowedGame;
        profile.TshCursorCaptureEnabledInWindowedMenu = request.TshCursorCaptureEnabledInWindowedMenu;
        profile.TshScreenEdgeScrollEnabledInFullscreenApp = request.TshScreenEdgeScrollEnabledInFullscreenApp;
        profile.TshScreenEdgeScrollEnabledInWindowedApp = request.TshScreenEdgeScrollEnabledInWindowedApp;
        profile.TshMoneyTransactionVolume = request.TshMoneyTransactionVolume;
        profile.TshGameWindowTransitionSpeedMultiplier = request.TshGameWindowTransitionSpeedMultiplier;

        // GeneralsOnline settings
        profile.GoShowFps = request.GoShowFps;
        profile.GoShowPing = request.GoShowPing;
        profile.GoShowPlayerRanks = request.GoShowPlayerRanks;
        profile.GoAutoLogin = request.GoAutoLogin;
        profile.GoRememberUsername = request.GoRememberUsername;
        profile.GoEnableNotifications = request.GoEnableNotifications;
        profile.GoEnableSoundNotifications = request.GoEnableSoundNotifications;
        profile.GoChatFontSize = request.GoChatFontSize;

        // Camera settings
        profile.GoCameraMaxHeightOnlyWhenLobbyHost = request.GoCameraMaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = request.GoCameraMinHeight;
        profile.GoCameraMoveSpeedRatio = request.GoCameraMoveSpeedRatio;

        // Chat settings
        profile.GoChatDurationSecondsUntilFadeOut = request.GoChatDurationSecondsUntilFadeOut;

        // Debug settings
        profile.GoDebugVerboseLogging = request.GoDebugVerboseLogging;

        // Render settings
        profile.GoRenderFpsLimit = request.GoRenderFpsLimit;
        profile.GoRenderLimitFramerate = request.GoRenderLimitFramerate;
        profile.GoRenderStatsOverlay = request.GoRenderStatsOverlay;

        // Social notification settings
        profile.GoSocialNotificationFriendComesOnlineGameplay = request.GoSocialNotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = request.GoSocialNotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = request.GoSocialNotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = request.GoSocialNotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = request.GoSocialNotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = request.GoSocialNotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = request.GoSocialNotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = request.GoSocialNotificationPlayerSendsRequestMenus;

        profile.GameSpyIPAddress = request.GameSpyIPAddress;
    }

    /// <summary>
    /// Populates settings from an UpdateProfileRequest into a GameProfile.
    /// </summary>
    /// <param name="profile">The GameProfile to populate.</param>
    /// <param name="request">The request containing the settings.</param>
    public static void PopulateGameProfile(GameProfile profile, UpdateProfileRequest request)
    {
        // Video settings
        profile.VideoResolutionWidth = request.VideoResolutionWidth;
        profile.VideoResolutionHeight = request.VideoResolutionHeight;
        profile.VideoWindowed = request.VideoWindowed;
        profile.VideoTextureQuality = request.VideoTextureQuality;
        profile.EnableVideoShadows = request.EnableVideoShadows;
        profile.VideoParticleEffects = request.VideoParticleEffects;
        profile.VideoExtraAnimations = request.VideoExtraAnimations;
        profile.VideoBuildingAnimations = request.VideoBuildingAnimations;
        profile.VideoGamma = request.VideoGamma;
        profile.VideoAlternateMouseSetup = request.VideoAlternateMouseSetup;
        profile.VideoHeatEffects = request.VideoHeatEffects;
        profile.VideoStaticGameLOD = request.VideoStaticGameLOD;
        profile.VideoIdealStaticGameLOD = request.VideoIdealStaticGameLOD;
        profile.VideoUseDoubleClickAttackMove = request.VideoUseDoubleClickAttackMove;
        profile.VideoScrollFactor = request.VideoScrollFactor;
        profile.VideoRetaliation = request.VideoRetaliation;
        profile.VideoDynamicLOD = request.VideoDynamicLOD;
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount;
        profile.VideoAntiAliasing = request.VideoAntiAliasing;
        profile.VideoUseLightMap = request.VideoUseLightMap;
        profile.VideoSkipEALogo = request.VideoSkipEALogo;
        profile.VideoUseShadowDecals = request.VideoUseShadowDecals;
        profile.VideoBuildingOcclusion = request.VideoBuildingOcclusion;
        profile.VideoShowProps = request.VideoShowProps;
        profile.VideoDrawScrollAnchor = request.VideoDrawScrollAnchor;
        profile.VideoMoveScrollAnchor = request.VideoMoveScrollAnchor;
        profile.VideoGameTimeFontSize = request.VideoGameTimeFontSize;
        profile.GameLanguageFilter = request.GameLanguageFilter;
        profile.NetworkSendDelay = request.NetworkSendDelay;
        profile.VideoShowSoftWaterEdge = request.VideoShowSoftWaterEdge;
        profile.VideoShowTrees = request.VideoShowTrees;
        profile.VideoUseCloudMap = request.VideoUseCloudMap;

        // Audio settings
        profile.AudioSoundVolume = request.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds;

        // TheSuperHackers settings
        profile.TshArchiveReplays = request.TshArchiveReplays;
        profile.TshShowMoneyPerMinute = request.TshShowMoneyPerMinute;
        profile.TshPlayerObserverEnabled = request.TshPlayerObserverEnabled;
        profile.TshSystemTimeFontSize = request.TshSystemTimeFontSize;
        profile.TshNetworkLatencyFontSize = request.TshNetworkLatencyFontSize;
        profile.TshRenderFpsFontSize = request.TshRenderFpsFontSize;
        profile.TshResolutionFontAdjustment = request.TshResolutionFontAdjustment;
        profile.TshCursorCaptureEnabledInFullscreenGame = request.TshCursorCaptureEnabledInFullscreenGame;
        profile.TshCursorCaptureEnabledInFullscreenMenu = request.TshCursorCaptureEnabledInFullscreenMenu;
        profile.TshCursorCaptureEnabledInWindowedGame = request.TshCursorCaptureEnabledInWindowedGame;
        profile.TshCursorCaptureEnabledInWindowedMenu = request.TshCursorCaptureEnabledInWindowedMenu;
        profile.TshScreenEdgeScrollEnabledInFullscreenApp = request.TshScreenEdgeScrollEnabledInFullscreenApp;
        profile.TshScreenEdgeScrollEnabledInWindowedApp = request.TshScreenEdgeScrollEnabledInWindowedApp;
        profile.TshMoneyTransactionVolume = request.TshMoneyTransactionVolume;
        profile.TshGameWindowTransitionSpeedMultiplier = request.TshGameWindowTransitionSpeedMultiplier;

        // GeneralsOnline settings
        profile.GoShowFps = request.GoShowFps;
        profile.GoShowPing = request.GoShowPing;
        profile.GoShowPlayerRanks = request.GoShowPlayerRanks;
        profile.GoAutoLogin = request.GoAutoLogin;
        profile.GoRememberUsername = request.GoRememberUsername;
        profile.GoEnableNotifications = request.GoEnableNotifications;
        profile.GoEnableSoundNotifications = request.GoEnableSoundNotifications;
        profile.GoChatFontSize = request.GoChatFontSize;

        // Camera settings
        profile.GoCameraMaxHeightOnlyWhenLobbyHost = request.GoCameraMaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = request.GoCameraMinHeight;
        profile.GoCameraMoveSpeedRatio = request.GoCameraMoveSpeedRatio;

        // Chat settings
        profile.GoChatDurationSecondsUntilFadeOut = request.GoChatDurationSecondsUntilFadeOut;

        // Debug settings
        profile.GoDebugVerboseLogging = request.GoDebugVerboseLogging;

        // Render settings
        profile.GoRenderFpsLimit = request.GoRenderFpsLimit;
        profile.GoRenderLimitFramerate = request.GoRenderLimitFramerate;
        profile.GoRenderStatsOverlay = request.GoRenderStatsOverlay;

        // Social notification settings
        profile.GoSocialNotificationFriendComesOnlineGameplay = request.GoSocialNotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = request.GoSocialNotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = request.GoSocialNotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = request.GoSocialNotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = request.GoSocialNotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = request.GoSocialNotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = request.GoSocialNotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = request.GoSocialNotificationPlayerSendsRequestMenus;

        profile.GameSpyIPAddress = request.GameSpyIPAddress;
    }

    /// <summary>
    /// Patches a GameProfile with non-null values from a CreateProfileRequest.
    /// </summary>
    /// <param name="profile">The GameProfile to patch.</param>
    /// <param name="request">The request containing potentially partial settings.</param>
    public static void PatchGameProfile(GameProfile profile, CreateProfileRequest request)
    {
        PatchVideoSettings(profile, request);
        PatchAudioSettings(profile, request);
        PatchTshSettings(profile, request);
        PatchGeneralsOnlineSettings(profile, request);

        profile.GameSpyIPAddress = request.GameSpyIPAddress ?? profile.GameSpyIPAddress;
        profile.VideoSkipEALogo = request.VideoSkipEALogo ?? profile.VideoSkipEALogo;
    }

    /// <summary>
    /// Patches a GameProfile with non-null values from an UpdateProfileRequest.
    /// </summary>
    /// <param name="profile">The GameProfile to patch.</param>
    /// <param name="request">The request containing potentially partial settings.</param>
    public static void UpdateFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        UpdateVideoFromRequest(profile, request);
        UpdateAudioFromRequest(profile, request);
        UpdateTshFromRequest(profile, request);
        UpdateGeneralsOnlineFromRequest(profile, request);

        if (request.UseSteamLaunch.HasValue)
            profile.UseSteamLaunch = request.UseSteamLaunch.Value;

        profile.GameSpyIPAddress = request.GameSpyIPAddress ?? profile.GameSpyIPAddress;
        profile.VideoSkipEALogo = request.VideoSkipEALogo ?? profile.VideoSkipEALogo;
    }

    /// <summary>
    /// Populates settings from one UpdateProfileRequest into a CreateProfileRequest.
    /// </summary>
    /// <param name="target">The target CreateProfileRequest.</param>
    /// <param name="source">The source UpdateProfileRequest.</param>
    public static void PopulateRequest(CreateProfileRequest target, UpdateProfileRequest source)
    {
        target.VideoResolutionWidth = source.VideoResolutionWidth;
        target.VideoResolutionHeight = source.VideoResolutionHeight;
        target.VideoWindowed = source.VideoWindowed;
        target.VideoTextureQuality = source.VideoTextureQuality;
        target.EnableVideoShadows = source.EnableVideoShadows;
        target.VideoParticleEffects = source.VideoParticleEffects;
        target.VideoExtraAnimations = source.VideoExtraAnimations;
        target.VideoBuildingAnimations = source.VideoBuildingAnimations;
        target.VideoGamma = source.VideoGamma;
        target.VideoAlternateMouseSetup = source.VideoAlternateMouseSetup;
        target.VideoHeatEffects = source.VideoHeatEffects;
        target.VideoStaticGameLOD = source.VideoStaticGameLOD;
        target.VideoIdealStaticGameLOD = source.VideoIdealStaticGameLOD;
        target.VideoUseDoubleClickAttackMove = source.VideoUseDoubleClickAttackMove;
        target.VideoScrollFactor = source.VideoScrollFactor;
        target.VideoRetaliation = source.VideoRetaliation;
        target.VideoDynamicLOD = source.VideoDynamicLOD;
        target.VideoMaxParticleCount = source.VideoMaxParticleCount;
        target.VideoAntiAliasing = source.VideoAntiAliasing;
        target.VideoDrawScrollAnchor = source.VideoDrawScrollAnchor;
        target.VideoMoveScrollAnchor = source.VideoMoveScrollAnchor;
        target.VideoGameTimeFontSize = source.VideoGameTimeFontSize;
        target.GameLanguageFilter = source.GameLanguageFilter;
        target.NetworkSendDelay = source.NetworkSendDelay;
        target.VideoShowSoftWaterEdge = source.VideoShowSoftWaterEdge;
        target.VideoShowTrees = source.VideoShowTrees;
        target.VideoUseCloudMap = source.VideoUseCloudMap;
        target.VideoUseLightMap = source.VideoUseLightMap;
        target.VideoSkipEALogo = source.VideoSkipEALogo;
        target.VideoUseShadowDecals = source.VideoUseShadowDecals;
        target.VideoBuildingOcclusion = source.VideoBuildingOcclusion;
        target.VideoShowProps = source.VideoShowProps;

        target.AudioSoundVolume = source.AudioSoundVolume;
        target.AudioThreeDSoundVolume = source.AudioThreeDSoundVolume;
        target.AudioSpeechVolume = source.AudioSpeechVolume;
        target.AudioMusicVolume = source.AudioMusicVolume;
        target.AudioEnabled = source.AudioEnabled;
        target.AudioNumSounds = source.AudioNumSounds;

        target.TshArchiveReplays = source.TshArchiveReplays;
        target.TshShowMoneyPerMinute = source.TshShowMoneyPerMinute;
        target.TshPlayerObserverEnabled = source.TshPlayerObserverEnabled;
        target.TshSystemTimeFontSize = source.TshSystemTimeFontSize;
        target.TshNetworkLatencyFontSize = source.TshNetworkLatencyFontSize;
        target.TshRenderFpsFontSize = source.TshRenderFpsFontSize;
        target.TshResolutionFontAdjustment = source.TshResolutionFontAdjustment;
        target.TshCursorCaptureEnabledInFullscreenGame = source.TshCursorCaptureEnabledInFullscreenGame;
        target.TshCursorCaptureEnabledInFullscreenMenu = source.TshCursorCaptureEnabledInFullscreenMenu;
        target.TshCursorCaptureEnabledInWindowedGame = source.TshCursorCaptureEnabledInWindowedGame;
        target.TshCursorCaptureEnabledInWindowedMenu = source.TshCursorCaptureEnabledInWindowedMenu;
        target.TshScreenEdgeScrollEnabledInFullscreenApp = source.TshScreenEdgeScrollEnabledInFullscreenApp;
        target.TshScreenEdgeScrollEnabledInWindowedApp = source.TshScreenEdgeScrollEnabledInWindowedApp;
        target.TshMoneyTransactionVolume = source.TshMoneyTransactionVolume;
        target.TshGameWindowTransitionSpeedMultiplier = source.TshGameWindowTransitionSpeedMultiplier;

        target.GoShowFps = source.GoShowFps;
        target.GoShowPing = source.GoShowPing;
        target.GoShowPlayerRanks = source.GoShowPlayerRanks;
        target.GoAutoLogin = source.GoAutoLogin;
        target.GoRememberUsername = source.GoRememberUsername;
        target.GoEnableNotifications = source.GoEnableNotifications;
        target.GoEnableSoundNotifications = source.GoEnableSoundNotifications;
        target.GoChatFontSize = source.GoChatFontSize;

        target.GoCameraMaxHeightOnlyWhenLobbyHost = source.GoCameraMaxHeightOnlyWhenLobbyHost;
        target.GoCameraMinHeight = source.GoCameraMinHeight;
        target.GoCameraMoveSpeedRatio = source.GoCameraMoveSpeedRatio;

        target.GoChatDurationSecondsUntilFadeOut = source.GoChatDurationSecondsUntilFadeOut;

        target.GoDebugVerboseLogging = source.GoDebugVerboseLogging;

        target.GoRenderFpsLimit = source.GoRenderFpsLimit;
        target.GoRenderLimitFramerate = source.GoRenderLimitFramerate;
        target.GoRenderStatsOverlay = source.GoRenderStatsOverlay;

        target.GoSocialNotificationFriendComesOnlineGameplay = source.GoSocialNotificationFriendComesOnlineGameplay;
        target.GoSocialNotificationFriendComesOnlineMenus = source.GoSocialNotificationFriendComesOnlineMenus;
        target.GoSocialNotificationFriendGoesOfflineGameplay = source.GoSocialNotificationFriendGoesOfflineGameplay;
        target.GoSocialNotificationFriendGoesOfflineMenus = source.GoSocialNotificationFriendGoesOfflineMenus;
        target.GoSocialNotificationPlayerAcceptsRequestGameplay = source.GoSocialNotificationPlayerAcceptsRequestGameplay;
        target.GoSocialNotificationPlayerAcceptsRequestMenus = source.GoSocialNotificationPlayerAcceptsRequestMenus;
        target.GoSocialNotificationPlayerSendsRequestGameplay = source.GoSocialNotificationPlayerSendsRequestGameplay;
        target.GoSocialNotificationPlayerSendsRequestMenus = source.GoSocialNotificationPlayerSendsRequestMenus;

        target.UseSteamLaunch = source.UseSteamLaunch;
        target.GameSpyIPAddress = source.GameSpyIPAddress;
    }

    /// <summary>
    /// Populates settings from one UpdateProfileRequest into another.
    /// </summary>
    /// <param name="target">The target UpdateProfileRequest.</param>
    /// <param name="source">The source UpdateProfileRequest.</param>
    public static void PopulateRequest(UpdateProfileRequest target, UpdateProfileRequest source)
    {
        target.VideoResolutionWidth = source.VideoResolutionWidth;
        target.VideoResolutionHeight = source.VideoResolutionHeight;
        target.VideoWindowed = source.VideoWindowed;
        target.VideoTextureQuality = source.VideoTextureQuality;
        target.EnableVideoShadows = source.EnableVideoShadows;
        target.VideoParticleEffects = source.VideoParticleEffects;
        target.VideoExtraAnimations = source.VideoExtraAnimations;
        target.VideoBuildingAnimations = source.VideoBuildingAnimations;
        target.VideoGamma = source.VideoGamma;
        target.VideoAlternateMouseSetup = source.VideoAlternateMouseSetup;
        target.VideoHeatEffects = source.VideoHeatEffects;
        target.VideoStaticGameLOD = source.VideoStaticGameLOD;
        target.VideoIdealStaticGameLOD = source.VideoIdealStaticGameLOD;
        target.VideoUseDoubleClickAttackMove = source.VideoUseDoubleClickAttackMove;
        target.VideoScrollFactor = source.VideoScrollFactor;
        target.VideoRetaliation = source.VideoRetaliation;
        target.VideoDynamicLOD = source.VideoDynamicLOD;
        target.VideoMaxParticleCount = source.VideoMaxParticleCount;
        target.VideoAntiAliasing = source.VideoAntiAliasing;
        target.VideoDrawScrollAnchor = source.VideoDrawScrollAnchor;
        target.VideoMoveScrollAnchor = source.VideoMoveScrollAnchor;
        target.VideoGameTimeFontSize = source.VideoGameTimeFontSize;
        target.GameLanguageFilter = source.GameLanguageFilter;
        target.NetworkSendDelay = source.NetworkSendDelay;
        target.VideoShowSoftWaterEdge = source.VideoShowSoftWaterEdge;
        target.VideoShowTrees = source.VideoShowTrees;
        target.VideoUseCloudMap = source.VideoUseCloudMap;
        target.VideoUseLightMap = source.VideoUseLightMap;
        target.VideoSkipEALogo = source.VideoSkipEALogo;
        target.VideoUseShadowDecals = source.VideoUseShadowDecals;
        target.VideoBuildingOcclusion = source.VideoBuildingOcclusion;
        target.VideoShowProps = source.VideoShowProps;

        target.AudioSoundVolume = source.AudioSoundVolume;
        target.AudioThreeDSoundVolume = source.AudioThreeDSoundVolume;
        target.AudioSpeechVolume = source.AudioSpeechVolume;
        target.AudioMusicVolume = source.AudioMusicVolume;
        target.AudioEnabled = source.AudioEnabled;
        target.AudioNumSounds = source.AudioNumSounds;

        target.TshArchiveReplays = source.TshArchiveReplays;
        target.TshShowMoneyPerMinute = source.TshShowMoneyPerMinute;
        target.TshPlayerObserverEnabled = source.TshPlayerObserverEnabled;
        target.TshSystemTimeFontSize = source.TshSystemTimeFontSize;
        target.TshNetworkLatencyFontSize = source.TshNetworkLatencyFontSize;
        target.TshRenderFpsFontSize = source.TshRenderFpsFontSize;
        target.TshResolutionFontAdjustment = source.TshResolutionFontAdjustment;
        target.TshCursorCaptureEnabledInFullscreenGame = source.TshCursorCaptureEnabledInFullscreenGame;
        target.TshCursorCaptureEnabledInFullscreenMenu = source.TshCursorCaptureEnabledInFullscreenMenu;
        target.TshCursorCaptureEnabledInWindowedGame = source.TshCursorCaptureEnabledInWindowedGame;
        target.TshCursorCaptureEnabledInWindowedMenu = source.TshCursorCaptureEnabledInWindowedMenu;
        target.TshScreenEdgeScrollEnabledInFullscreenApp = source.TshScreenEdgeScrollEnabledInFullscreenApp;
        target.TshScreenEdgeScrollEnabledInWindowedApp = source.TshScreenEdgeScrollEnabledInWindowedApp;
        target.TshMoneyTransactionVolume = source.TshMoneyTransactionVolume;
        target.TshGameWindowTransitionSpeedMultiplier = source.TshGameWindowTransitionSpeedMultiplier;

        target.GoShowFps = source.GoShowFps;
        target.GoShowPing = source.GoShowPing;
        target.GoShowPlayerRanks = source.GoShowPlayerRanks;
        target.GoAutoLogin = source.GoAutoLogin;
        target.GoRememberUsername = source.GoRememberUsername;
        target.GoEnableNotifications = source.GoEnableNotifications;
        target.GoEnableSoundNotifications = source.GoEnableSoundNotifications;
        target.GoChatFontSize = source.GoChatFontSize;

        target.GoCameraMaxHeightOnlyWhenLobbyHost = source.GoCameraMaxHeightOnlyWhenLobbyHost;
        target.GoCameraMinHeight = source.GoCameraMinHeight;
        target.GoCameraMoveSpeedRatio = source.GoCameraMoveSpeedRatio;

        target.GoChatDurationSecondsUntilFadeOut = source.GoChatDurationSecondsUntilFadeOut;

        target.GoDebugVerboseLogging = source.GoDebugVerboseLogging;

        target.GoRenderFpsLimit = source.GoRenderFpsLimit;
        target.GoRenderLimitFramerate = source.GoRenderLimitFramerate;
        target.GoRenderStatsOverlay = source.GoRenderStatsOverlay;

        target.GoSocialNotificationFriendComesOnlineGameplay = source.GoSocialNotificationFriendComesOnlineGameplay;
        target.GoSocialNotificationFriendComesOnlineMenus = source.GoSocialNotificationFriendComesOnlineMenus;
        target.GoSocialNotificationFriendGoesOfflineGameplay = source.GoSocialNotificationFriendGoesOfflineGameplay;
        target.GoSocialNotificationFriendGoesOfflineMenus = source.GoSocialNotificationFriendGoesOfflineMenus;
        target.GoSocialNotificationPlayerAcceptsRequestGameplay = source.GoSocialNotificationPlayerAcceptsRequestGameplay;
        target.GoSocialNotificationPlayerAcceptsRequestMenus = source.GoSocialNotificationPlayerAcceptsRequestMenus;
        target.GoSocialNotificationPlayerSendsRequestGameplay = source.GoSocialNotificationPlayerSendsRequestGameplay;
        target.GoSocialNotificationPlayerSendsRequestMenus = source.GoSocialNotificationPlayerSendsRequestMenus;

        target.UseSteamLaunch = source.UseSteamLaunch;
        target.GameSpyIPAddress = source.GameSpyIPAddress;
    }

    /// <summary>
    /// Normalizes and clamps a transition speed multiplier value to the supported range.
    /// </summary>
    /// <param name="value">The float value.</param>
    /// <returns>The clamped float multiplier if finite and non-null; otherwise, null.</returns>
    public static float? NormalizeTransitionSpeedMultiplier(float? value)
    {
        if (value.HasValue && float.IsFinite(value.Value))
        {
            return Math.Clamp(
                value.Value,
                GameSettingsTheSuperHackersConstants.MinGameWindowTransitionSpeedMultiplier,
                GameSettingsTheSuperHackersConstants.MaxGameWindowTransitionSpeedMultiplier);
        }

        return null;
    }

    /// <summary>
    /// Parses and clamps the GameWindowTransitionSpeedMultiplier from a raw string value.
    /// </summary>
    /// <param name="value">The raw string value.</param>
    /// <returns>The clamped float multiplier if valid; otherwise, null.</returns>
    public static float? ParseTransitionSpeedMultiplier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ||
            float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out speed))
        {
            return NormalizeTransitionSpeedMultiplier(speed);
        }

        return null;
    }

    private static void ApplyVideoFromOptions(IniOptions options, GameProfile profile)
    {
        profile.VideoResolutionWidth = options.Video.ResolutionWidth;
        profile.VideoResolutionHeight = options.Video.ResolutionHeight;
        profile.VideoWindowed = options.Video.Windowed;

        // Convert TextureReduction back to TextureQuality
        profile.VideoTextureQuality = options.Video.TextureReduction switch
        {
            GameSettingsConstants.TextureQuality.TextureReductionLow => TextureQuality.Low,
            GameSettingsConstants.TextureQuality.TextureReductionMedium => TextureQuality.Medium,
            GameSettingsConstants.TextureQuality.TextureReductionHigh => TextureQuality.High,
            _ => null,
        };

        profile.EnableVideoShadows = options.Video.UseShadowVolumes;
        profile.VideoUseShadowDecals = options.Video.UseShadowDecals;
        profile.VideoBuildingOcclusion = options.Video.BuildingOcclusion;
        profile.VideoShowProps = options.Video.ShowProps;

        if (options.Video.AdditionalProperties.TryGetValue("GenHubBuildingAnimations", out var ba))
            profile.VideoBuildingAnimations = ParseBool(ba);

        if (options.Video.AdditionalProperties.TryGetValue("GenHubParticleEffects", out var pe))
            profile.VideoParticleEffects = ParseBool(pe);

        profile.VideoExtraAnimations = options.Video.ExtraAnimations;
        profile.VideoGamma = options.Video.Gamma;
        profile.VideoAlternateMouseSetup = options.Video.AlternateMouseSetup;
        profile.VideoHeatEffects = options.Video.HeatEffects;

        // Load additional video settings from root (Flat format support)
        if (options.Video.AdditionalProperties.TryGetValue("StaticGameLOD", out var staticLOD))
            profile.VideoStaticGameLOD = staticLOD;
        if (options.Video.AdditionalProperties.TryGetValue("IdealStaticGameLOD", out var idealLOD))
            profile.VideoIdealStaticGameLOD = idealLOD;

        if (options.Video.AdditionalProperties.TryGetValue("SkipEALogo", out var sel))
            profile.VideoSkipEALogo = ParseBool(sel);

        if (options.Video.AdditionalProperties.TryGetValue("ShowSoftWaterEdge", out var swe))
            profile.VideoShowSoftWaterEdge = ParseBool(swe);
        if (options.Video.AdditionalProperties.TryGetValue("ShowTrees", out var st))
            profile.VideoShowTrees = ParseBool(st);
        if (options.Video.AdditionalProperties.TryGetValue("UseCloudMap", out var ucm))
            profile.VideoUseCloudMap = ParseBool(ucm);
        if (options.Video.AdditionalProperties.TryGetValue("UseLightMap", out var ulm))
            profile.VideoUseLightMap = ParseBool(ulm);

        profile.VideoAntiAliasing ??= options.Video.AntiAliasing;

        ApplyTshFlatSettingsFromOptions(options, profile);
        ApplyTshHierarchicalSettingsFromOptions(options, profile);
    }

    private static void ApplyTshFlatSettingsFromOptions(IniOptions options, GameProfile profile)
    {
        ApplyTshProperties(options.Video.AdditionalProperties, profile);
    }

    private static void ApplyTshHierarchicalSettingsFromOptions(IniOptions options, GameProfile profile)
    {
        var tshKvp = options.AdditionalSections.FirstOrDefault(s =>
            string.Equals(s.Key, GameSettingsTheSuperHackersConstants.SectionName, StringComparison.OrdinalIgnoreCase));
        if (tshKvp.Value is { Count: > 0 })
        {
            ApplyTshProperties(tshKvp.Value, profile);
        }
    }

    private static void ApplyTshProperties(Dictionary<string, string> tsh, GameProfile profile)
    {
        ApplyTshVideoSettings(tsh, profile);
        ApplyTshFeatureSettings(tsh, profile);
        ApplyTshFontSettings(tsh, profile);
        ApplyTshCursorSettings(tsh, profile);
        ApplyTshScrollAndAudioSettings(tsh, profile);
    }

    private static void ApplyTshVideoSettings(Dictionary<string, string> tsh, GameProfile profile)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.UseDoubleClickAttackMoveKey, out var doubleClickTsh))
            profile.VideoUseDoubleClickAttackMove = ParseBool(doubleClickTsh);
        else if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.UseDoubleClickKey, out var dblTsh))
            profile.VideoUseDoubleClickAttackMove = ParseBool(dblTsh);

        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ScrollFactorKey, out var scrollTsh) && int.TryParse(scrollTsh, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scrollTshVal))
            profile.VideoScrollFactor = scrollTshVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.RetaliationKey, out var retaliationTsh))
            profile.VideoRetaliation = ParseBool(retaliationTsh);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.DynamicLODKey, out var dynLODTsh))
            profile.VideoDynamicLOD = ParseBool(dynLODTsh);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.MaxParticleCountKey, out var particlesTsh) && int.TryParse(particlesTsh, NumberStyles.Integer, CultureInfo.InvariantCulture, out var particlesTshVal))
            profile.VideoMaxParticleCount = particlesTshVal;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.GameWindowTransitionSpeedMultiplierKey, out var speedTsh))
        {
            var parsed = ParseTransitionSpeedMultiplier(speedTsh);
            if (parsed.HasValue)
            {
                profile.TshGameWindowTransitionSpeedMultiplier = parsed.Value;
            }
        }

        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey, out var dsa))
            profile.VideoDrawScrollAnchor = ParseBool(dsa);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey, out var msa))
            profile.VideoMoveScrollAnchor = ParseBool(msa);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.LanguageFilterKey, out var lf))
            profile.GameLanguageFilter = ParseBool(lf);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.SendDelayKey, out var sd))
            profile.NetworkSendDelay = ParseBool(sd);
    }

    private static void ApplyTshFeatureSettings(Dictionary<string, string> tsh, GameProfile profile)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ArchiveReplaysKey, out var archiveReplays))
            profile.TshArchiveReplays = ParseBool(archiveReplays);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ShowMoneyPerMinuteKey, out var showMoney))
            profile.TshShowMoneyPerMinute = ParseBool(showMoney);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.PlayerObserverEnabledKey, out var playerObserver))
            profile.TshPlayerObserverEnabled = ParseBool(playerObserver);
    }

    private static void ApplyTshFontSettings(Dictionary<string, string> tsh, GameProfile profile)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.SystemTimeFontSizeKey, out var sysTimeFont) && int.TryParse(sysTimeFont, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stf))
            profile.TshSystemTimeFontSize = stf;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.NetworkLatencyFontSizeKey, out var netLatencyFont) && int.TryParse(netLatencyFont, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nlf))
            profile.TshNetworkLatencyFontSize = nlf;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.RenderFpsFontSizeKey, out var fpsFont) && int.TryParse(fpsFont, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ff))
            profile.TshRenderFpsFontSize = ff;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ResolutionFontAdjustmentKey, out var resFontAdj) && int.TryParse(resFontAdj, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rfa))
            profile.TshResolutionFontAdjustment = rfa;
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey, out var gtfs) && int.TryParse(gtfs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gtfVal))
            profile.VideoGameTimeFontSize = gtfVal;
    }

    private static void ApplyTshCursorSettings(Dictionary<string, string> tsh, GameProfile profile)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenGameKey, out var cursorFullscreenGame))
            profile.TshCursorCaptureEnabledInFullscreenGame = ParseBool(cursorFullscreenGame);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenMenuKey, out var cursorFullscreenMenu))
            profile.TshCursorCaptureEnabledInFullscreenMenu = ParseBool(cursorFullscreenMenu);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedGameKey, out var cursorWindowedGame))
            profile.TshCursorCaptureEnabledInWindowedGame = ParseBool(cursorWindowedGame);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedMenuKey, out var cursorWindowedMenu))
            profile.TshCursorCaptureEnabledInWindowedMenu = ParseBool(cursorWindowedMenu);
    }

    private static void ApplyTshScrollAndAudioSettings(Dictionary<string, string> tsh, GameProfile profile)
    {
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInFullscreenAppKey, out var scrollFullscreen))
            profile.TshScreenEdgeScrollEnabledInFullscreenApp = ParseBool(scrollFullscreen);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInWindowedAppKey, out var scrollWindowed))
            profile.TshScreenEdgeScrollEnabledInWindowedApp = ParseBool(scrollWindowed);
        if (tsh.TryGetCaseInsensitive(GameSettingsTheSuperHackersConstants.MoneyTransactionVolumeKey, out var moneyVolume) && int.TryParse(moneyVolume, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mv))
            profile.TshMoneyTransactionVolume = mv;
    }

    private static void ApplyAudioFromOptions(IniOptions options, GameProfile profile)
    {
        profile.AudioSoundVolume = options.Audio.SFXVolume;
        profile.AudioThreeDSoundVolume = options.Audio.SFX3DVolume;
        profile.AudioSpeechVolume = options.Audio.VoiceVolume;
        profile.AudioMusicVolume = options.Audio.MusicVolume;
        profile.AudioEnabled = options.Audio.AudioEnabled;
        profile.AudioNumSounds = options.Audio.NumSounds;
    }

    private static void ApplyNetworkFromOptions(IniOptions options, GameProfile profile)
    {
        profile.GameSpyIPAddress = options.Network.GameSpyIPAddress;
        if (options.Network.AdditionalProperties.TryGetValue(GameSettingsTheSuperHackersConstants.SendDelayKey, out var sd))
            profile.NetworkSendDelay = ParseBool(sd);
        if (options.Network.AdditionalProperties.TryGetValue(GameSettingsTheSuperHackersConstants.LanguageFilterKey, out var lf))
            profile.GameLanguageFilter = ParseBool(lf);
    }

    private static void ApplyGoGeneralSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        if (profile.GoShowFps.HasValue) settings.ShowFps = profile.GoShowFps.Value;
        if (profile.GoShowPing.HasValue) settings.ShowPing = profile.GoShowPing.Value;
        if (profile.GoShowPlayerRanks.HasValue) settings.ShowPlayerRanks = profile.GoShowPlayerRanks.Value;
        if (profile.GoAutoLogin.HasValue) settings.AutoLogin = profile.GoAutoLogin.Value;
        if (profile.GoRememberUsername.HasValue) settings.RememberUsername = profile.GoRememberUsername.Value;
        if (profile.GoEnableNotifications.HasValue) settings.EnableNotifications = profile.GoEnableNotifications.Value;
        if (profile.GoEnableSoundNotifications.HasValue) settings.EnableSoundNotifications = profile.GoEnableSoundNotifications.Value;
        if (profile.GoChatFontSize.HasValue) settings.ChatFontSize = profile.GoChatFontSize.Value;
    }

    private static void ApplyGoCameraAndChatSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        if (profile.GoCameraMaxHeightOnlyWhenLobbyHost.HasValue) settings.Camera.MaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost.Value;
        if (profile.GoCameraMinHeight.HasValue) settings.Camera.MinHeight = profile.GoCameraMinHeight.Value;
        if (profile.GoCameraMoveSpeedRatio.HasValue) settings.Camera.MoveSpeedRatio = profile.GoCameraMoveSpeedRatio.Value;
        if (profile.GoChatDurationSecondsUntilFadeOut.HasValue) settings.Chat.DurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut.Value;
    }

    private static void ApplyGoRenderAndDebugSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        if (profile.GoDebugVerboseLogging.HasValue) settings.Debug.VerboseLogging = profile.GoDebugVerboseLogging.Value;
        if (profile.GoRenderFpsLimit.HasValue) settings.Render.FpsLimit = profile.GoRenderFpsLimit.Value;
        if (profile.GoRenderLimitFramerate.HasValue) settings.Render.LimitFramerate = profile.GoRenderLimitFramerate.Value;
        if (profile.GoRenderStatsOverlay.HasValue) settings.Render.StatsOverlay = profile.GoRenderStatsOverlay.Value;
    }

    private static void ApplyGoSocialSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        if (profile.GoSocialNotificationFriendComesOnlineGameplay.HasValue) settings.Social.NotificationFriendComesOnlineGameplay = profile.GoSocialNotificationFriendComesOnlineGameplay.Value;
        if (profile.GoSocialNotificationFriendComesOnlineMenus.HasValue) settings.Social.NotificationFriendComesOnlineMenus = profile.GoSocialNotificationFriendComesOnlineMenus.Value;
        if (profile.GoSocialNotificationFriendGoesOfflineGameplay.HasValue) settings.Social.NotificationFriendGoesOfflineGameplay = profile.GoSocialNotificationFriendGoesOfflineGameplay.Value;
        if (profile.GoSocialNotificationFriendGoesOfflineMenus.HasValue) settings.Social.NotificationFriendGoesOfflineMenus = profile.GoSocialNotificationFriendGoesOfflineMenus.Value;
        if (profile.GoSocialNotificationPlayerAcceptsRequestGameplay.HasValue) settings.Social.NotificationPlayerAcceptsRequestGameplay = profile.GoSocialNotificationPlayerAcceptsRequestGameplay.Value;
        if (profile.GoSocialNotificationPlayerAcceptsRequestMenus.HasValue) settings.Social.NotificationPlayerAcceptsRequestMenus = profile.GoSocialNotificationPlayerAcceptsRequestMenus.Value;
        if (profile.GoSocialNotificationPlayerSendsRequestGameplay.HasValue) settings.Social.NotificationPlayerSendsRequestGameplay = profile.GoSocialNotificationPlayerSendsRequestGameplay.Value;
        if (profile.GoSocialNotificationPlayerSendsRequestMenus.HasValue) settings.Social.NotificationPlayerSendsRequestMenus = profile.GoSocialNotificationPlayerSendsRequestMenus.Value;
    }

    private static void ApplyVideoResolutionAndQualityToOptions(GameProfile profile, IniOptions options, ILogger? logger)
    {
        if (profile.VideoResolutionWidth.HasValue)
        {
            if (profile.VideoResolutionWidth.Value >= GameSettingsConstants.Resolution.MinWidth &&
                profile.VideoResolutionWidth.Value <= GameSettingsConstants.Resolution.MaxWidth)
            {
                options.Video.ResolutionWidth = profile.VideoResolutionWidth.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid VideoResolutionWidth {Width} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.VideoResolutionWidth.Value,
                    profile.Id,
                    GameSettingsConstants.Resolution.MinWidth,
                    GameSettingsConstants.Resolution.MaxWidth);
            }
        }

        if (profile.VideoResolutionHeight.HasValue)
        {
            if (profile.VideoResolutionHeight.Value >= GameSettingsConstants.Resolution.MinHeight &&
                profile.VideoResolutionHeight.Value <= GameSettingsConstants.Resolution.MaxHeight)
            {
                options.Video.ResolutionHeight = profile.VideoResolutionHeight.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid VideoResolutionHeight {Height} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.VideoResolutionHeight.Value,
                    profile.Id,
                    GameSettingsConstants.Resolution.MinHeight,
                    GameSettingsConstants.Resolution.MaxHeight);
            }
        }

        if (profile.VideoWindowed.HasValue)
            options.Video.Windowed = profile.VideoWindowed.Value;

        if (profile.VideoTextureQuality.HasValue)
        {
            options.Video.TextureReduction = profile.VideoTextureQuality.Value switch
            {
                TextureQuality.Low => GameSettingsConstants.TextureQuality.TextureReductionLow,
                TextureQuality.Medium => GameSettingsConstants.TextureQuality.TextureReductionMedium,
                TextureQuality.High or TextureQuality.VeryHigh => GameSettingsConstants.TextureQuality.TextureReductionHigh,
                _ => options.Video.TextureReduction,
            };
        }
    }

    private static void ApplyVideoAdditionalToOptions(GameProfile profile, IniOptions options, ILogger? logger)
    {
        ApplyVideoVisualEffectsToOptions(profile, options, logger);
        ApplyVideoGameplayToOptions(profile, options);
        ApplyVideoRenderingTogglesToOptions(profile, options);
    }

    private static void ApplyVideoVisualEffectsToOptions(GameProfile profile, IniOptions options, ILogger? logger)
    {
        if (profile.EnableVideoShadows.HasValue)
        {
            options.Video.UseShadowVolumes = profile.EnableVideoShadows.Value;
            options.Video.AdditionalProperties["UseShadowVolumes"] = profile.EnableVideoShadows.Value ? "yes" : "no";
        }

        if (profile.VideoExtraAnimations.HasValue)
        {
            options.Video.ExtraAnimations = profile.VideoExtraAnimations.Value;
            options.Video.AdditionalProperties["ExtraAnimations"] = profile.VideoExtraAnimations.Value ? "yes" : "no";
        }

        if (profile.VideoGamma.HasValue)
        {
            if (profile.VideoGamma.Value >= GameSettingsConstants.Gamma.Min &&
                profile.VideoGamma.Value <= GameSettingsConstants.Gamma.Max)
            {
                options.Video.Gamma = profile.VideoGamma.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid VideoGamma {Gamma} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.VideoGamma.Value,
                    profile.Id,
                    GameSettingsConstants.Gamma.Min,
                    GameSettingsConstants.Gamma.Max);
            }
        }

        if (profile.VideoAlternateMouseSetup.HasValue)
        {
            options.Video.AlternateMouseSetup = profile.VideoAlternateMouseSetup.Value;
            options.Video.AdditionalProperties["UseAlternateMouse"] = profile.VideoAlternateMouseSetup.Value ? "yes" : "no";
        }

        if (profile.VideoHeatEffects.HasValue)
            options.Video.HeatEffects = profile.VideoHeatEffects.Value;

        if (profile.VideoBuildingAnimations.HasValue)
            options.Video.AdditionalProperties["GenHubBuildingAnimations"] = profile.VideoBuildingAnimations.Value ? "yes" : "no";

        if (profile.VideoParticleEffects.HasValue)
            options.Video.AdditionalProperties["GenHubParticleEffects"] = profile.VideoParticleEffects.Value ? "yes" : "no";
    }

    private static void ApplyVideoGameplayToOptions(GameProfile profile, IniOptions options)
    {
        if (profile.VideoStaticGameLOD != null)
            options.Video.AdditionalProperties["StaticGameLOD"] = profile.VideoStaticGameLOD;

        if (profile.VideoIdealStaticGameLOD != null)
            options.Video.AdditionalProperties["IdealStaticGameLOD"] = profile.VideoIdealStaticGameLOD;

        if (profile.VideoAntiAliasing.HasValue)
            options.Video.AntiAliasing = profile.VideoAntiAliasing.Value;

        if (profile.VideoUseDoubleClickAttackMove.HasValue)
        {
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.UseDoubleClickAttackMoveKey] = profile.VideoUseDoubleClickAttackMove.Value ? "yes" : "no";
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.UseDoubleClickKey] = profile.VideoUseDoubleClickAttackMove.Value ? "yes" : "no";
        }

        if (profile.VideoScrollFactor.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.ScrollFactorKey] = profile.VideoScrollFactor.Value.ToString();

        if (profile.VideoRetaliation.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.RetaliationKey] = profile.VideoRetaliation.Value ? "yes" : "no";

        if (profile.VideoDynamicLOD.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.DynamicLODKey] = profile.VideoDynamicLOD.Value ? "yes" : "no";

        if (profile.VideoMaxParticleCount.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.MaxParticleCountKey] = profile.VideoMaxParticleCount.Value.ToString();

        if (profile.VideoSkipEALogo.HasValue)
            options.Video.AdditionalProperties["SkipEALogo"] = profile.VideoSkipEALogo.Value ? "yes" : "no";

        if (profile.VideoGameTimeFontSize.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey] = profile.VideoGameTimeFontSize.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static void ApplyVideoRenderingTogglesToOptions(GameProfile profile, IniOptions options)
    {
        if (profile.VideoDrawScrollAnchor.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey] = profile.VideoDrawScrollAnchor.Value ? "yes" : "no";

        if (profile.VideoMoveScrollAnchor.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey] = profile.VideoMoveScrollAnchor.Value ? "yes" : "no";

        if (profile.GameLanguageFilter.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.LanguageFilterKey] = profile.GameLanguageFilter.Value ? "yes" : "no";

        if (profile.NetworkSendDelay.HasValue)
            options.Video.AdditionalProperties[GameSettingsTheSuperHackersConstants.SendDelayKey] = profile.NetworkSendDelay.Value ? "yes" : "no";

        if (profile.VideoShowSoftWaterEdge.HasValue)
            options.Video.AdditionalProperties["ShowSoftWaterEdge"] = profile.VideoShowSoftWaterEdge.Value ? "yes" : "no";

        if (profile.VideoShowTrees.HasValue)
            options.Video.AdditionalProperties["ShowTrees"] = profile.VideoShowTrees.Value ? "yes" : "no";

        if (profile.VideoUseCloudMap.HasValue)
            options.Video.AdditionalProperties["UseCloudMap"] = profile.VideoUseCloudMap.Value ? "yes" : "no";

        if (profile.VideoUseLightMap.HasValue)
            options.Video.AdditionalProperties["UseLightMap"] = profile.VideoUseLightMap.Value ? "yes" : "no";

        if (profile.VideoUseShadowDecals.HasValue)
            options.Video.UseShadowDecals = profile.VideoUseShadowDecals.Value;

        if (profile.VideoBuildingOcclusion.HasValue)
            options.Video.BuildingOcclusion = profile.VideoBuildingOcclusion.Value;

        if (profile.VideoShowProps.HasValue)
            options.Video.ShowProps = profile.VideoShowProps.Value;
    }

    private static void ApplyAudioToOptions(GameProfile profile, IniOptions options, ILogger? logger)
    {
        if (profile.AudioSoundVolume.HasValue)
        {
            if (profile.AudioSoundVolume.Value >= GameSettingsConstants.Volume.Min &&
                profile.AudioSoundVolume.Value <= GameSettingsConstants.Volume.Max)
            {
                options.Audio.SFXVolume = profile.AudioSoundVolume.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid AudioSoundVolume {Volume} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.AudioSoundVolume.Value,
                    profile.Id,
                    GameSettingsConstants.Volume.Min,
                    GameSettingsConstants.Volume.Max);
            }
        }

        if (profile.AudioThreeDSoundVolume.HasValue)
        {
            if (profile.AudioThreeDSoundVolume.Value >= GameSettingsConstants.Volume.Min &&
                profile.AudioThreeDSoundVolume.Value <= GameSettingsConstants.Volume.Max)
            {
                options.Audio.SFX3DVolume = profile.AudioThreeDSoundVolume.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid AudioThreeDSoundVolume {Volume} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.AudioThreeDSoundVolume.Value,
                    profile.Id,
                    GameSettingsConstants.Volume.Min,
                    GameSettingsConstants.Volume.Max);
            }
        }

        if (profile.AudioSpeechVolume.HasValue)
        {
            if (profile.AudioSpeechVolume.Value >= GameSettingsConstants.Volume.Min &&
                profile.AudioSpeechVolume.Value <= GameSettingsConstants.Volume.Max)
            {
                options.Audio.VoiceVolume = profile.AudioSpeechVolume.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid AudioSpeechVolume {Volume} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.AudioSpeechVolume.Value,
                    profile.Id,
                    GameSettingsConstants.Volume.Min,
                    GameSettingsConstants.Volume.Max);
            }
        }

        if (profile.AudioMusicVolume.HasValue)
        {
            if (profile.AudioMusicVolume.Value >= GameSettingsConstants.Volume.Min &&
                profile.AudioMusicVolume.Value <= GameSettingsConstants.Volume.Max)
            {
                options.Audio.MusicVolume = profile.AudioMusicVolume.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid AudioMusicVolume {Volume} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.AudioMusicVolume.Value,
                    profile.Id,
                    GameSettingsConstants.Volume.Min,
                    GameSettingsConstants.Volume.Max);
            }
        }

        if (profile.AudioEnabled.HasValue)
        {
            options.Audio.AudioEnabled = profile.AudioEnabled.Value;
        }

        if (profile.AudioNumSounds.HasValue)
        {
            if (profile.AudioNumSounds.Value >= GameSettingsConstants.Audio.MinNumSounds &&
                profile.AudioNumSounds.Value <= GameSettingsConstants.Audio.MaxNumSounds)
            {
                options.Audio.NumSounds = profile.AudioNumSounds.Value;
            }
            else
            {
                logger?.LogWarning(
                    "Invalid AudioNumSounds {NumSounds} for profile {ProfileId}, must be {Min}-{Max}",
                    profile.AudioNumSounds.Value,
                    profile.Id,
                    GameSettingsConstants.Audio.MinNumSounds,
                    GameSettingsConstants.Audio.MaxNumSounds);
            }
        }
    }

    private static void ApplyTshToOptions(GameProfile profile, IniOptions options)
    {
        var tshKey = options.AdditionalSections.Keys.FirstOrDefault(k =>
            string.Equals(k, GameSettingsTheSuperHackersConstants.SectionName, StringComparison.OrdinalIgnoreCase)) ?? GameSettingsTheSuperHackersConstants.SectionName;

        Dictionary<string, string> tshDict;
        if (options.AdditionalSections.TryGetValue(tshKey, out var existingTsh) && existingTsh != null)
        {
            tshDict = existingTsh;
        }
        else
        {
            tshDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            options.AdditionalSections[tshKey] = tshDict;
        }

        ApplyTshUiSettingsToDict(profile, tshDict);
        ApplyTshControlsSettingsToDict(profile, tshDict);

        if (profile.VideoGameTimeFontSize.HasValue && tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey))
            tshDict[GameSettingsTheSuperHackersConstants.GameTimeFontSizeKey] = profile.VideoGameTimeFontSize.Value.ToString(CultureInfo.InvariantCulture);
        if (profile.VideoDrawScrollAnchor.HasValue && tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey))
            tshDict[GameSettingsTheSuperHackersConstants.DrawScrollAnchorKey] = BoolToString(profile.VideoDrawScrollAnchor.Value);
        if (profile.VideoMoveScrollAnchor.HasValue && tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey))
            tshDict[GameSettingsTheSuperHackersConstants.MoveScrollAnchorKey] = BoolToString(profile.VideoMoveScrollAnchor.Value);
        if (profile.GameLanguageFilter.HasValue && tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.LanguageFilterKey))
            tshDict[GameSettingsTheSuperHackersConstants.LanguageFilterKey] = BoolToString(profile.GameLanguageFilter.Value);
        if (profile.NetworkSendDelay.HasValue && tshDict.ContainsKey(GameSettingsTheSuperHackersConstants.SendDelayKey))
            tshDict[GameSettingsTheSuperHackersConstants.SendDelayKey] = BoolToString(profile.NetworkSendDelay.Value);
    }

    private static void ApplyTshUiSettingsToDict(GameProfile profile, Dictionary<string, string> tshDict)
    {
        if (profile.TshArchiveReplays.HasValue) tshDict[GameSettingsTheSuperHackersConstants.ArchiveReplaysKey] = BoolToString(profile.TshArchiveReplays.Value);
        if (profile.TshShowMoneyPerMinute.HasValue) tshDict[GameSettingsTheSuperHackersConstants.ShowMoneyPerMinuteKey] = BoolToString(profile.TshShowMoneyPerMinute.Value);
        if (profile.TshPlayerObserverEnabled.HasValue) tshDict[GameSettingsTheSuperHackersConstants.PlayerObserverEnabledKey] = BoolToString(profile.TshPlayerObserverEnabled.Value);
        if (profile.TshSystemTimeFontSize.HasValue) tshDict[GameSettingsTheSuperHackersConstants.SystemTimeFontSizeKey] = profile.TshSystemTimeFontSize.Value.ToString(CultureInfo.InvariantCulture);
        if (profile.TshNetworkLatencyFontSize.HasValue) tshDict[GameSettingsTheSuperHackersConstants.NetworkLatencyFontSizeKey] = profile.TshNetworkLatencyFontSize.Value.ToString(CultureInfo.InvariantCulture);
        if (profile.TshRenderFpsFontSize.HasValue) tshDict[GameSettingsTheSuperHackersConstants.RenderFpsFontSizeKey] = profile.TshRenderFpsFontSize.Value.ToString(CultureInfo.InvariantCulture);
        if (profile.TshResolutionFontAdjustment.HasValue) tshDict[GameSettingsTheSuperHackersConstants.ResolutionFontAdjustmentKey] = profile.TshResolutionFontAdjustment.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static void ApplyTshControlsSettingsToDict(GameProfile profile, Dictionary<string, string> tshDict)
    {
        if (profile.TshCursorCaptureEnabledInFullscreenGame.HasValue) tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenGameKey] = BoolToString(profile.TshCursorCaptureEnabledInFullscreenGame.Value);
        if (profile.TshCursorCaptureEnabledInFullscreenMenu.HasValue) tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInFullscreenMenuKey] = BoolToString(profile.TshCursorCaptureEnabledInFullscreenMenu.Value);
        if (profile.TshCursorCaptureEnabledInWindowedGame.HasValue) tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedGameKey] = BoolToString(profile.TshCursorCaptureEnabledInWindowedGame.Value);
        if (profile.TshCursorCaptureEnabledInWindowedMenu.HasValue) tshDict[GameSettingsTheSuperHackersConstants.CursorCaptureEnabledInWindowedMenuKey] = BoolToString(profile.TshCursorCaptureEnabledInWindowedMenu.Value);
        if (profile.TshScreenEdgeScrollEnabledInFullscreenApp.HasValue) tshDict[GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInFullscreenAppKey] = BoolToString(profile.TshScreenEdgeScrollEnabledInFullscreenApp.Value);
        if (profile.TshScreenEdgeScrollEnabledInWindowedApp.HasValue) tshDict[GameSettingsTheSuperHackersConstants.ScreenEdgeScrollEnabledInWindowedAppKey] = BoolToString(profile.TshScreenEdgeScrollEnabledInWindowedApp.Value);
        if (profile.TshMoneyTransactionVolume.HasValue) tshDict[GameSettingsTheSuperHackersConstants.MoneyTransactionVolumeKey] = profile.TshMoneyTransactionVolume.Value.ToString(CultureInfo.InvariantCulture);
        if (NormalizeTransitionSpeedMultiplier(profile.TshGameWindowTransitionSpeedMultiplier) is { } speedMultiplier)
        {
            tshDict[GameSettingsTheSuperHackersConstants.GameWindowTransitionSpeedMultiplierKey] = speedMultiplier.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void PatchVideoSettings(GameProfile profile, CreateProfileRequest request)
    {
        PatchBasicVideoSettings(profile, request);
        PatchAdvancedVideoSettings(profile, request);
    }

    private static void PatchBasicVideoSettings(GameProfile profile, CreateProfileRequest request)
    {
        profile.VideoResolutionWidth = request.VideoResolutionWidth ?? profile.VideoResolutionWidth;
        profile.VideoResolutionHeight = request.VideoResolutionHeight ?? profile.VideoResolutionHeight;
        profile.VideoWindowed = request.VideoWindowed ?? profile.VideoWindowed;
        profile.VideoTextureQuality = request.VideoTextureQuality ?? profile.VideoTextureQuality;
        profile.EnableVideoShadows = request.EnableVideoShadows ?? profile.EnableVideoShadows;
        profile.VideoParticleEffects = request.VideoParticleEffects ?? profile.VideoParticleEffects;
        profile.VideoExtraAnimations = request.VideoExtraAnimations ?? profile.VideoExtraAnimations;
        profile.VideoBuildingAnimations = request.VideoBuildingAnimations ?? profile.VideoBuildingAnimations;
        profile.VideoGamma = request.VideoGamma ?? profile.VideoGamma;
        profile.VideoAlternateMouseSetup = request.VideoAlternateMouseSetup ?? profile.VideoAlternateMouseSetup;
        profile.VideoHeatEffects = request.VideoHeatEffects ?? profile.VideoHeatEffects;
        profile.VideoAntiAliasing = request.VideoAntiAliasing ?? profile.VideoAntiAliasing;
    }

    private static void PatchAdvancedVideoSettings(GameProfile profile, CreateProfileRequest request)
    {
        profile.VideoStaticGameLOD = request.VideoStaticGameLOD ?? profile.VideoStaticGameLOD;
        profile.VideoIdealStaticGameLOD = request.VideoIdealStaticGameLOD ?? profile.VideoIdealStaticGameLOD;
        profile.VideoUseDoubleClickAttackMove = request.VideoUseDoubleClickAttackMove ?? profile.VideoUseDoubleClickAttackMove;
        profile.VideoScrollFactor = request.VideoScrollFactor ?? profile.VideoScrollFactor;
        profile.VideoRetaliation = request.VideoRetaliation ?? profile.VideoRetaliation;
        profile.VideoDynamicLOD = request.VideoDynamicLOD ?? profile.VideoDynamicLOD;
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount ?? profile.VideoMaxParticleCount;
        profile.VideoSkipEALogo = request.VideoSkipEALogo ?? profile.VideoSkipEALogo;
        profile.VideoDrawScrollAnchor = request.VideoDrawScrollAnchor ?? profile.VideoDrawScrollAnchor;
        profile.VideoMoveScrollAnchor = request.VideoMoveScrollAnchor ?? profile.VideoMoveScrollAnchor;
        profile.VideoGameTimeFontSize = request.VideoGameTimeFontSize ?? profile.VideoGameTimeFontSize;
        profile.GameLanguageFilter = request.GameLanguageFilter ?? profile.GameLanguageFilter;
        profile.NetworkSendDelay = request.NetworkSendDelay ?? profile.NetworkSendDelay;
        profile.VideoShowSoftWaterEdge = request.VideoShowSoftWaterEdge ?? profile.VideoShowSoftWaterEdge;
        profile.VideoShowTrees = request.VideoShowTrees ?? profile.VideoShowTrees;
        profile.VideoUseCloudMap = request.VideoUseCloudMap ?? profile.VideoUseCloudMap;
        profile.VideoUseLightMap = request.VideoUseLightMap ?? profile.VideoUseLightMap;
        profile.VideoUseShadowDecals = request.VideoUseShadowDecals ?? profile.VideoUseShadowDecals;
        profile.VideoBuildingOcclusion = request.VideoBuildingOcclusion ?? profile.VideoBuildingOcclusion;
        profile.VideoShowProps = request.VideoShowProps ?? profile.VideoShowProps;
    }

    private static void PatchAudioSettings(GameProfile profile, CreateProfileRequest request)
    {
        profile.AudioSoundVolume = request.AudioSoundVolume ?? profile.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume ?? profile.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume ?? profile.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume ?? profile.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled ?? profile.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds ?? profile.AudioNumSounds;
    }

    private static void PatchTshSettings(GameProfile profile, CreateProfileRequest request)
    {
        profile.TshArchiveReplays = request.TshArchiveReplays ?? profile.TshArchiveReplays;
        profile.TshShowMoneyPerMinute = request.TshShowMoneyPerMinute ?? profile.TshShowMoneyPerMinute;
        profile.TshPlayerObserverEnabled = request.TshPlayerObserverEnabled ?? profile.TshPlayerObserverEnabled;
        profile.TshSystemTimeFontSize = request.TshSystemTimeFontSize ?? profile.TshSystemTimeFontSize;
        profile.TshNetworkLatencyFontSize = request.TshNetworkLatencyFontSize ?? profile.TshNetworkLatencyFontSize;
        profile.TshRenderFpsFontSize = request.TshRenderFpsFontSize ?? profile.TshRenderFpsFontSize;
        profile.TshResolutionFontAdjustment = request.TshResolutionFontAdjustment ?? profile.TshResolutionFontAdjustment;
        profile.TshCursorCaptureEnabledInFullscreenGame = request.TshCursorCaptureEnabledInFullscreenGame ?? profile.TshCursorCaptureEnabledInFullscreenGame;
        profile.TshCursorCaptureEnabledInFullscreenMenu = request.TshCursorCaptureEnabledInFullscreenMenu ?? profile.TshCursorCaptureEnabledInFullscreenMenu;
        profile.TshCursorCaptureEnabledInWindowedGame = request.TshCursorCaptureEnabledInWindowedGame ?? profile.TshCursorCaptureEnabledInWindowedGame;
        profile.TshCursorCaptureEnabledInWindowedMenu = request.TshCursorCaptureEnabledInWindowedMenu ?? profile.TshCursorCaptureEnabledInWindowedMenu;
        profile.TshScreenEdgeScrollEnabledInFullscreenApp = request.TshScreenEdgeScrollEnabledInFullscreenApp ?? profile.TshScreenEdgeScrollEnabledInFullscreenApp;
        profile.TshScreenEdgeScrollEnabledInWindowedApp = request.TshScreenEdgeScrollEnabledInWindowedApp ?? profile.TshScreenEdgeScrollEnabledInWindowedApp;
        profile.TshMoneyTransactionVolume = request.TshMoneyTransactionVolume ?? profile.TshMoneyTransactionVolume;
        profile.TshGameWindowTransitionSpeedMultiplier = request.TshGameWindowTransitionSpeedMultiplier ?? profile.TshGameWindowTransitionSpeedMultiplier;
    }

    private static void PatchGeneralsOnlineSettings(GameProfile profile, CreateProfileRequest request)
    {
        profile.GoShowFps = request.GoShowFps ?? profile.GoShowFps;
        profile.GoShowPing = request.GoShowPing ?? profile.GoShowPing;
        profile.GoShowPlayerRanks = request.GoShowPlayerRanks ?? profile.GoShowPlayerRanks;
        profile.GoAutoLogin = request.GoAutoLogin ?? profile.GoAutoLogin;
        profile.GoRememberUsername = request.GoRememberUsername ?? profile.GoRememberUsername;
        profile.GoEnableNotifications = request.GoEnableNotifications ?? profile.GoEnableNotifications;
        profile.GoEnableSoundNotifications = request.GoEnableSoundNotifications ?? profile.GoEnableSoundNotifications;
        profile.GoChatFontSize = request.GoChatFontSize ?? profile.GoChatFontSize;

        profile.GoCameraMaxHeightOnlyWhenLobbyHost = request.GoCameraMaxHeightOnlyWhenLobbyHost ?? profile.GoCameraMaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = request.GoCameraMinHeight ?? profile.GoCameraMinHeight;
        profile.GoCameraMoveSpeedRatio = request.GoCameraMoveSpeedRatio ?? profile.GoCameraMoveSpeedRatio;
        profile.GoChatDurationSecondsUntilFadeOut = request.GoChatDurationSecondsUntilFadeOut ?? profile.GoChatDurationSecondsUntilFadeOut;
        profile.GoDebugVerboseLogging = request.GoDebugVerboseLogging ?? profile.GoDebugVerboseLogging;

        profile.GoRenderFpsLimit = request.GoRenderFpsLimit ?? profile.GoRenderFpsLimit;
        profile.GoRenderLimitFramerate = request.GoRenderLimitFramerate ?? profile.GoRenderLimitFramerate;
        profile.GoRenderStatsOverlay = request.GoRenderStatsOverlay ?? profile.GoRenderStatsOverlay;

        profile.GoSocialNotificationFriendComesOnlineGameplay = request.GoSocialNotificationFriendComesOnlineGameplay ?? profile.GoSocialNotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = request.GoSocialNotificationFriendComesOnlineMenus ?? profile.GoSocialNotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = request.GoSocialNotificationFriendGoesOfflineGameplay ?? profile.GoSocialNotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = request.GoSocialNotificationFriendGoesOfflineMenus ?? profile.GoSocialNotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = request.GoSocialNotificationPlayerAcceptsRequestGameplay ?? profile.GoSocialNotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = request.GoSocialNotificationPlayerAcceptsRequestMenus ?? profile.GoSocialNotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = request.GoSocialNotificationPlayerSendsRequestGameplay ?? profile.GoSocialNotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = request.GoSocialNotificationPlayerSendsRequestMenus ?? profile.GoSocialNotificationPlayerSendsRequestMenus;
    }

    private static void UpdateVideoFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        UpdateBasicVideoFromRequest(profile, request);
        UpdateAdvancedVideoFromRequest(profile, request);
    }

    private static void UpdateBasicVideoFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        profile.VideoResolutionWidth = request.VideoResolutionWidth ?? profile.VideoResolutionWidth;
        profile.VideoResolutionHeight = request.VideoResolutionHeight ?? profile.VideoResolutionHeight;
        profile.VideoWindowed = request.VideoWindowed ?? profile.VideoWindowed;
        profile.VideoTextureQuality = request.VideoTextureQuality ?? profile.VideoTextureQuality;
        profile.EnableVideoShadows = request.EnableVideoShadows ?? profile.EnableVideoShadows;
        profile.VideoParticleEffects = request.VideoParticleEffects ?? profile.VideoParticleEffects;
        profile.VideoExtraAnimations = request.VideoExtraAnimations ?? profile.VideoExtraAnimations;
        profile.VideoBuildingAnimations = request.VideoBuildingAnimations ?? profile.VideoBuildingAnimations;
        profile.VideoGamma = request.VideoGamma ?? profile.VideoGamma;
        profile.VideoAlternateMouseSetup = request.VideoAlternateMouseSetup ?? profile.VideoAlternateMouseSetup;
        profile.VideoHeatEffects = request.VideoHeatEffects ?? profile.VideoHeatEffects;
        profile.VideoAntiAliasing = request.VideoAntiAliasing ?? profile.VideoAntiAliasing;
    }

    private static void UpdateAdvancedVideoFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        profile.VideoStaticGameLOD = request.VideoStaticGameLOD ?? profile.VideoStaticGameLOD;
        profile.VideoIdealStaticGameLOD = request.VideoIdealStaticGameLOD ?? profile.VideoIdealStaticGameLOD;
        profile.VideoUseDoubleClickAttackMove = request.VideoUseDoubleClickAttackMove ?? profile.VideoUseDoubleClickAttackMove;
        profile.VideoScrollFactor = request.VideoScrollFactor ?? profile.VideoScrollFactor;
        profile.VideoRetaliation = request.VideoRetaliation ?? profile.VideoRetaliation;
        profile.VideoDynamicLOD = request.VideoDynamicLOD ?? profile.VideoDynamicLOD;
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount ?? profile.VideoMaxParticleCount;
        profile.VideoSkipEALogo = request.VideoSkipEALogo ?? profile.VideoSkipEALogo;
        profile.VideoUseLightMap = request.VideoUseLightMap ?? profile.VideoUseLightMap;
        profile.VideoUseShadowDecals = request.VideoUseShadowDecals ?? profile.VideoUseShadowDecals;
        profile.VideoBuildingOcclusion = request.VideoBuildingOcclusion ?? profile.VideoBuildingOcclusion;
        profile.VideoShowProps = request.VideoShowProps ?? profile.VideoShowProps;
        profile.VideoDrawScrollAnchor = request.VideoDrawScrollAnchor ?? profile.VideoDrawScrollAnchor;
        profile.VideoMoveScrollAnchor = request.VideoMoveScrollAnchor ?? profile.VideoMoveScrollAnchor;
        profile.VideoGameTimeFontSize = request.VideoGameTimeFontSize ?? profile.VideoGameTimeFontSize;
        profile.GameLanguageFilter = request.GameLanguageFilter ?? profile.GameLanguageFilter;
        profile.NetworkSendDelay = request.NetworkSendDelay ?? profile.NetworkSendDelay;
        profile.VideoShowSoftWaterEdge = request.VideoShowSoftWaterEdge ?? profile.VideoShowSoftWaterEdge;
        profile.VideoShowTrees = request.VideoShowTrees ?? profile.VideoShowTrees;
        profile.VideoUseCloudMap = request.VideoUseCloudMap ?? profile.VideoUseCloudMap;
    }

    private static void UpdateAudioFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        profile.AudioSoundVolume = request.AudioSoundVolume ?? profile.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume ?? profile.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume ?? profile.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume ?? profile.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled ?? profile.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds ?? profile.AudioNumSounds;
    }

    private static void UpdateTshFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        profile.TshArchiveReplays = request.TshArchiveReplays ?? profile.TshArchiveReplays;
        profile.TshShowMoneyPerMinute = request.TshShowMoneyPerMinute ?? profile.TshShowMoneyPerMinute;
        profile.TshPlayerObserverEnabled = request.TshPlayerObserverEnabled ?? profile.TshPlayerObserverEnabled;
        profile.TshSystemTimeFontSize = request.TshSystemTimeFontSize ?? profile.TshSystemTimeFontSize;
        profile.TshNetworkLatencyFontSize = request.TshNetworkLatencyFontSize ?? profile.TshNetworkLatencyFontSize;
        profile.TshRenderFpsFontSize = request.TshRenderFpsFontSize ?? profile.TshRenderFpsFontSize;
        profile.TshResolutionFontAdjustment = request.TshResolutionFontAdjustment ?? profile.TshResolutionFontAdjustment;
        profile.TshCursorCaptureEnabledInFullscreenGame = request.TshCursorCaptureEnabledInFullscreenGame ?? profile.TshCursorCaptureEnabledInFullscreenGame;
        profile.TshCursorCaptureEnabledInFullscreenMenu = request.TshCursorCaptureEnabledInFullscreenMenu ?? profile.TshCursorCaptureEnabledInFullscreenMenu;
        profile.TshCursorCaptureEnabledInWindowedGame = request.TshCursorCaptureEnabledInWindowedGame ?? profile.TshCursorCaptureEnabledInWindowedGame;
        profile.TshCursorCaptureEnabledInWindowedMenu = request.TshCursorCaptureEnabledInWindowedMenu ?? profile.TshCursorCaptureEnabledInWindowedMenu;
        profile.TshScreenEdgeScrollEnabledInFullscreenApp = request.TshScreenEdgeScrollEnabledInFullscreenApp ?? profile.TshScreenEdgeScrollEnabledInFullscreenApp;
        profile.TshScreenEdgeScrollEnabledInWindowedApp = request.TshScreenEdgeScrollEnabledInWindowedApp ?? profile.TshScreenEdgeScrollEnabledInWindowedApp;
        profile.TshMoneyTransactionVolume = request.TshMoneyTransactionVolume ?? profile.TshMoneyTransactionVolume;
        profile.TshGameWindowTransitionSpeedMultiplier = request.TshGameWindowTransitionSpeedMultiplier ?? profile.TshGameWindowTransitionSpeedMultiplier;
    }

    private static void UpdateGeneralsOnlineFromRequest(GameProfile profile, UpdateProfileRequest request)
    {
        profile.GoShowFps = request.GoShowFps ?? profile.GoShowFps;
        profile.GoShowPing = request.GoShowPing ?? profile.GoShowPing;
        profile.GoShowPlayerRanks = request.GoShowPlayerRanks ?? profile.GoShowPlayerRanks;
        profile.GoAutoLogin = request.GoAutoLogin ?? profile.GoAutoLogin;
        profile.GoRememberUsername = request.GoRememberUsername ?? profile.GoRememberUsername;
        profile.GoEnableNotifications = request.GoEnableNotifications ?? profile.GoEnableNotifications;
        profile.GoEnableSoundNotifications = request.GoEnableSoundNotifications ?? profile.GoEnableSoundNotifications;
        profile.GoChatFontSize = request.GoChatFontSize ?? profile.GoChatFontSize;

        profile.GoCameraMaxHeightOnlyWhenLobbyHost = request.GoCameraMaxHeightOnlyWhenLobbyHost ?? profile.GoCameraMaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = request.GoCameraMinHeight ?? profile.GoCameraMinHeight;
        profile.GoCameraMoveSpeedRatio = request.GoCameraMoveSpeedRatio ?? profile.GoCameraMoveSpeedRatio;
        profile.GoChatDurationSecondsUntilFadeOut = request.GoChatDurationSecondsUntilFadeOut ?? profile.GoChatDurationSecondsUntilFadeOut;
        profile.GoDebugVerboseLogging = request.GoDebugVerboseLogging ?? profile.GoDebugVerboseLogging;

        profile.GoRenderFpsLimit = request.GoRenderFpsLimit ?? profile.GoRenderFpsLimit;
        profile.GoRenderLimitFramerate = request.GoRenderLimitFramerate ?? profile.GoRenderLimitFramerate;
        profile.GoRenderStatsOverlay = request.GoRenderStatsOverlay ?? profile.GoRenderStatsOverlay;

        profile.GoSocialNotificationFriendComesOnlineGameplay = request.GoSocialNotificationFriendComesOnlineGameplay ?? profile.GoSocialNotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = request.GoSocialNotificationFriendComesOnlineMenus ?? profile.GoSocialNotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = request.GoSocialNotificationFriendGoesOfflineGameplay ?? profile.GoSocialNotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = request.GoSocialNotificationFriendGoesOfflineMenus ?? profile.GoSocialNotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = request.GoSocialNotificationPlayerAcceptsRequestGameplay ?? profile.GoSocialNotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = request.GoSocialNotificationPlayerAcceptsRequestMenus ?? profile.GoSocialNotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = request.GoSocialNotificationPlayerSendsRequestGameplay ?? profile.GoSocialNotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = request.GoSocialNotificationPlayerSendsRequestMenus ?? profile.GoSocialNotificationPlayerSendsRequestMenus;
    }

    private static bool ParseBool(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value == "1";

    private static string BoolToString(bool value) => value ? "yes" : "no";
}
