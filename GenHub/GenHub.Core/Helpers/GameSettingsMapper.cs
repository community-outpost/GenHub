using GenHub.Core.Constants;
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
        // Video settings
        profile.VideoResolutionWidth = options.Video.ResolutionWidth;
        profile.VideoResolutionHeight = options.Video.ResolutionHeight;
        profile.VideoWindowed = options.Video.Windowed;

        // Convert TextureReduction back to TextureQuality
        profile.VideoTextureQuality = options.Video.TextureReduction switch
        {
            GameSettingsConstants.TextureQuality.TextureReductionLow => TextureQuality.Low,
            GameSettingsConstants.TextureQuality.TextureReductionMedium => TextureQuality.Medium,
            GameSettingsConstants.TextureQuality.TextureReductionHigh => TextureQuality.High,
            _ => null // Invalid or custom scaling handled elsewhere
        };

        profile.EnableVideoShadows = options.Video.UseShadowVolumes;

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

        profile.VideoAntiAliasing ??= options.Video.AntiAliasing;

        // TSH settings from root (Flat format support)
        if (options.Video.AdditionalProperties.TryGetValue("UseDoubleClickAttackMove", out var doubleClick))
            profile.VideoUseDoubleClickAttackMove = ParseBool(doubleClick);
        else if (options.Video.AdditionalProperties.TryGetValue("UseDoubleClick", out var dbl))
            profile.VideoUseDoubleClickAttackMove = ParseBool(dbl);

        if (options.Video.AdditionalProperties.TryGetValue("ScrollFactor", out var scroll) && int.TryParse(scroll, out var scrollVal))
            profile.VideoScrollFactor = scrollVal;
        if (options.Video.AdditionalProperties.TryGetValue("Retaliation", out var retaliation))
            profile.VideoRetaliation = ParseBool(retaliation);
        if (options.Video.AdditionalProperties.TryGetValue("DynamicLOD", out var dynLOD))
            profile.VideoDynamicLOD = ParseBool(dynLOD);
        if (options.Video.AdditionalProperties.TryGetValue("MaxParticleCount", out var particles) && int.TryParse(particles, out var particleVal))
            profile.VideoMaxParticleCount = particleVal;

        // TSH-specific settings from the [TheSuperHackers] section (Hierarchical format support)
        if (options.AdditionalSections.TryGetValue("TheSuperHackers", out var tsh))
        {
            if (tsh.TryGetValue("UseDoubleClickAttackMove", out var doubleClickTsh))
                profile.VideoUseDoubleClickAttackMove = ParseBool(doubleClickTsh);
            if (tsh.TryGetValue("ScrollFactor", out var scrollTsh) && int.TryParse(scrollTsh, out var scrollTshVal))
                profile.VideoScrollFactor = scrollTshVal;
            if (tsh.TryGetValue("Retaliation", out var retaliationTsh))
                profile.VideoRetaliation = ParseBool(retaliationTsh);
            if (tsh.TryGetValue("DynamicLOD", out var dynLODTsh))
                profile.VideoDynamicLOD = ParseBool(dynLODTsh);
            if (tsh.TryGetValue("MaxParticleCount", out var particlesTsh) && int.TryParse(particlesTsh, out var particlesTshVal))
                profile.VideoMaxParticleCount = particlesTshVal;
        }

        // Audio settings
        profile.AudioSoundVolume = options.Audio.SFXVolume;
        profile.AudioThreeDSoundVolume = options.Audio.SFX3DVolume;
        profile.AudioSpeechVolume = options.Audio.VoiceVolume;
        profile.AudioMusicVolume = options.Audio.MusicVolume;
        profile.AudioEnabled = options.Audio.AudioEnabled;
        profile.AudioNumSounds = options.Audio.NumSounds;

        // Network settings
        profile.GameSpyIPAddress = options.Network.GameSpyIPAddress;
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
        profile.GoCameraMaxHeightOnlyWhenLobbyHost = settings.CameraMaxHeightOnlyWhenLobbyHost;
        profile.GoCameraMinHeight = settings.CameraMinHeight;
        profile.GoCameraMoveSpeedRatio = settings.CameraMoveSpeedRatio;

        // Chat settings
        profile.GoChatDurationSecondsUntilFadeOut = settings.ChatDurationSecondsUntilFadeOut;

        // Debug settings
        profile.GoDebugVerboseLogging = settings.DebugVerboseLogging;

        // Render settings
        profile.GoRenderFpsLimit = settings.RenderFpsLimit;
        profile.GoRenderLimitFramerate = settings.RenderLimitFramerate;
        profile.GoRenderStatsOverlay = settings.RenderStatsOverlay;

        // Social notification settings
        profile.GoSocialNotificationFriendComesOnlineGameplay = settings.SocialNotificationFriendComesOnlineGameplay;
        profile.GoSocialNotificationFriendComesOnlineMenus = settings.SocialNotificationFriendComesOnlineMenus;
        profile.GoSocialNotificationFriendGoesOfflineGameplay = settings.SocialNotificationFriendGoesOfflineGameplay;
        profile.GoSocialNotificationFriendGoesOfflineMenus = settings.SocialNotificationFriendGoesOfflineMenus;
        profile.GoSocialNotificationPlayerAcceptsRequestGameplay = settings.SocialNotificationPlayerAcceptsRequestGameplay;
        profile.GoSocialNotificationPlayerAcceptsRequestMenus = settings.SocialNotificationPlayerAcceptsRequestMenus;
        profile.GoSocialNotificationPlayerSendsRequestGameplay = settings.SocialNotificationPlayerSendsRequestGameplay;
        profile.GoSocialNotificationPlayerSendsRequestMenus = settings.SocialNotificationPlayerSendsRequestMenus;

        // TSH settings (that exist in GeneralsOnlineSettings via inheritance)
        profile.TshArchiveReplays = settings.ArchiveReplays;
        profile.TshMoneyTransactionVolume = settings.MoneyTransactionVolume;
        profile.TshShowMoneyPerMinute = settings.ShowMoneyPerMinute;
        profile.TshPlayerObserverEnabled = settings.PlayerObserverEnabled;
        profile.TshSystemTimeFontSize = settings.SystemTimeFontSize;
        profile.TshNetworkLatencyFontSize = settings.NetworkLatencyFontSize;
        profile.TshRenderFpsFontSize = settings.RenderFpsFontSize;
        profile.TshResolutionFontAdjustment = settings.ResolutionFontAdjustment;
        profile.TshCursorCaptureEnabledInFullscreenGame = settings.CursorCaptureEnabledInFullscreenGame;
        profile.TshCursorCaptureEnabledInFullscreenMenu = settings.CursorCaptureEnabledInFullscreenMenu;
        profile.TshCursorCaptureEnabledInWindowedGame = settings.CursorCaptureEnabledInWindowedGame;
        profile.TshCursorCaptureEnabledInWindowedMenu = settings.CursorCaptureEnabledInWindowedMenu;
        profile.TshScreenEdgeScrollEnabledInFullscreenApp = settings.ScreenEdgeScrollEnabledInFullscreenApp;
        profile.TshScreenEdgeScrollEnabledInWindowedApp = settings.ScreenEdgeScrollEnabledInWindowedApp;
    }

    /// <summary>
    /// Applies settings from a GameProfile to a GeneralsOnlineSettings object.
    /// Used by GameLauncher to prepare settings.json for launch.
    /// </summary>
    /// <param name="profile">The GameProfile source.</param>
    /// <param name="settings">The GeneralsOnlineSettings to populate.</param>
    public static void ApplyToGeneralsOnlineSettings(GameProfile profile, GeneralsOnlineSettings settings)
    {
        // GeneralsOnline settings - use null-coalescing with model defaults
        // This ensures predictable behavior: always set a value, never rely on constructor defaults
        settings.ShowFps = profile.GoShowFps ?? false;
        settings.ShowPing = profile.GoShowPing ?? true;
        settings.ShowPlayerRanks = profile.GoShowPlayerRanks ?? true;
        settings.AutoLogin = profile.GoAutoLogin ?? false;
        settings.RememberUsername = profile.GoRememberUsername ?? true;
        settings.EnableNotifications = profile.GoEnableNotifications ?? true;
        settings.EnableSoundNotifications = profile.GoEnableSoundNotifications ?? true;
        settings.ChatFontSize = profile.GoChatFontSize ?? 12;

        // Camera settings
        settings.CameraMaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost ?? 310.0f;
        settings.CameraMinHeight = profile.GoCameraMinHeight ?? 310.0f;
        settings.CameraMoveSpeedRatio = profile.GoCameraMoveSpeedRatio ?? 1.5f;

        // Chat settings
        settings.ChatDurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut ?? 30;

        // Debug settings
        settings.DebugVerboseLogging = profile.GoDebugVerboseLogging ?? false;

        // Render settings
        settings.RenderFpsLimit = profile.GoRenderFpsLimit ?? 144;
        settings.RenderLimitFramerate = profile.GoRenderLimitFramerate ?? true;
        settings.RenderStatsOverlay = profile.GoRenderStatsOverlay ?? true;

        // Social notification settings
        settings.SocialNotificationFriendComesOnlineGameplay = profile.GoSocialNotificationFriendComesOnlineGameplay ?? true;
        settings.SocialNotificationFriendComesOnlineMenus = profile.GoSocialNotificationFriendComesOnlineMenus ?? true;
        settings.SocialNotificationFriendGoesOfflineGameplay = profile.GoSocialNotificationFriendGoesOfflineGameplay ?? true;
        settings.SocialNotificationFriendGoesOfflineMenus = profile.GoSocialNotificationFriendGoesOfflineMenus ?? true;
        settings.SocialNotificationPlayerAcceptsRequestGameplay = profile.GoSocialNotificationPlayerAcceptsRequestGameplay ?? true;
        settings.SocialNotificationPlayerAcceptsRequestMenus = profile.GoSocialNotificationPlayerAcceptsRequestMenus ?? true;
        settings.SocialNotificationPlayerSendsRequestGameplay = profile.GoSocialNotificationPlayerSendsRequestGameplay ?? true;
        settings.SocialNotificationPlayerSendsRequestMenus = profile.GoSocialNotificationPlayerSendsRequestMenus ?? true;

        // TSH settings (that exist in settings.json) - use null-coalescing with defaults
        settings.ArchiveReplays = profile.TshArchiveReplays ?? false;
        settings.MoneyTransactionVolume = profile.TshMoneyTransactionVolume ?? 50;
        settings.ShowMoneyPerMinute = profile.TshShowMoneyPerMinute ?? false;
        settings.PlayerObserverEnabled = profile.TshPlayerObserverEnabled ?? false;
        settings.SystemTimeFontSize = profile.TshSystemTimeFontSize ?? 12;
        settings.NetworkLatencyFontSize = profile.TshNetworkLatencyFontSize ?? 12;
        settings.RenderFpsFontSize = profile.TshRenderFpsFontSize ?? 12;
        settings.ResolutionFontAdjustment = profile.TshResolutionFontAdjustment ?? -100;
        settings.CursorCaptureEnabledInFullscreenGame = profile.TshCursorCaptureEnabledInFullscreenGame ?? false;
        settings.CursorCaptureEnabledInFullscreenMenu = profile.TshCursorCaptureEnabledInFullscreenMenu ?? false;
        settings.CursorCaptureEnabledInWindowedGame = profile.TshCursorCaptureEnabledInWindowedGame ?? false;
        settings.CursorCaptureEnabledInWindowedMenu = profile.TshCursorCaptureEnabledInWindowedMenu ?? false;
        settings.ScreenEdgeScrollEnabledInFullscreenApp = profile.TshScreenEdgeScrollEnabledInFullscreenApp ?? false;
        settings.ScreenEdgeScrollEnabledInWindowedApp = profile.TshScreenEdgeScrollEnabledInWindowedApp ?? false;
    }

    /// <summary>
    /// Applies profile settings to IniOptions with validation.
    /// </summary>
    /// <param name="profile">The game profile containing the settings.</param>
    /// <param name="options">The IniOptions object to apply settings to.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    public static void ApplyToOptions(GameProfile profile, IniOptions options, ILogger? logger = null)
    {
        // Video settings with validation
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
        {
            options.Video.Windowed = profile.VideoWindowed.Value;
        }

        if (profile.VideoTextureQuality.HasValue)
        {
            // Engine Value (TextureReduction): 2=Low, 1=Medium, 0=High/Max
            // Clamp anything higher than 'High' to Max Quality to prevent invalid values
            options.Video.TextureReduction = profile.VideoTextureQuality.Value switch
            {
                TextureQuality.Low => GameSettingsConstants.TextureQuality.TextureReductionLow,
                TextureQuality.Medium => GameSettingsConstants.TextureQuality.TextureReductionMedium,
                TextureQuality.High => GameSettingsConstants.TextureQuality.TextureReductionHigh,
                TextureQuality.VeryHigh => GameSettingsConstants.TextureQuality.TextureReductionHigh,
                _ => GameSettingsConstants.TextureQuality.TextureReductionHigh,
            };
        }

        if (profile.EnableVideoShadows.HasValue)
        {
            options.Video.UseShadowVolumes = profile.EnableVideoShadows.Value;
            options.Video.UseShadowDecals = profile.EnableVideoShadows.Value;
        }

        if (profile.VideoExtraAnimations.HasValue)
        {
            options.Video.ExtraAnimations = profile.VideoExtraAnimations.Value;
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
        }

        if (profile.VideoHeatEffects.HasValue)
        {
            options.Video.HeatEffects = profile.VideoHeatEffects.Value;
        }

        // Additional video settings to AdditionalProperties (Standard root)
        if (profile.VideoStaticGameLOD != null)
            options.Video.AdditionalProperties["StaticGameLOD"] = profile.VideoStaticGameLOD;
        if (profile.VideoIdealStaticGameLOD != null)
            options.Video.AdditionalProperties["IdealStaticGameLOD"] = profile.VideoIdealStaticGameLOD;
        if (profile.VideoAntiAliasing.HasValue)
            options.Video.AntiAliasing = profile.VideoAntiAliasing.Value;

        // TSH settings (writing to root for maximum compatibility as some clients prefer flat Options.ini)
        if (profile.VideoUseDoubleClickAttackMove.HasValue)
        {
            options.Video.AdditionalProperties["UseDoubleClickAttackMove"] = profile.VideoUseDoubleClickAttackMove.Value ? "yes" : "no";
            options.Video.AdditionalProperties["UseDoubleClick"] = profile.VideoUseDoubleClickAttackMove.Value ? "yes" : "no";
        }

        if (profile.VideoScrollFactor.HasValue)
            options.Video.AdditionalProperties["ScrollFactor"] = profile.VideoScrollFactor.Value.ToString();
        if (profile.VideoRetaliation.HasValue)
            options.Video.AdditionalProperties["Retaliation"] = profile.VideoRetaliation.Value ? "yes" : "no";
        if (profile.VideoDynamicLOD.HasValue)
            options.Video.AdditionalProperties["DynamicLOD"] = profile.VideoDynamicLOD.Value ? "yes" : "no";
        if (profile.VideoMaxParticleCount.HasValue)
            options.Video.AdditionalProperties["MaxParticleCount"] = profile.VideoMaxParticleCount.Value.ToString();
        if (profile.VideoSkipEALogo.HasValue)
            options.Video.AdditionalProperties["SkipEALogo"] = profile.VideoSkipEALogo.Value ? "yes" : "no";

        // Mirror Alternate Mouse
        if (profile.VideoAlternateMouseSetup.HasValue)
            options.Video.AdditionalProperties["UseAlternateMouse"] = profile.VideoAlternateMouseSetup.Value ? "yes" : "no";

        // Audio settings with validation
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

        // TheSuperHackers settings
        var tshDict = new Dictionary<string, string>();
        if (profile.TshArchiveReplays.HasValue) tshDict["ArchiveReplays"] = BoolToString(profile.TshArchiveReplays.Value);
        if (profile.TshShowMoneyPerMinute.HasValue) tshDict["ShowMoneyPerMinute"] = BoolToString(profile.TshShowMoneyPerMinute.Value);
        if (profile.TshPlayerObserverEnabled.HasValue) tshDict["PlayerObserverEnabled"] = BoolToString(profile.TshPlayerObserverEnabled.Value);
        if (profile.TshSystemTimeFontSize.HasValue) tshDict["SystemTimeFontSize"] = profile.TshSystemTimeFontSize.Value.ToString();
        if (profile.TshNetworkLatencyFontSize.HasValue) tshDict["NetworkLatencyFontSize"] = profile.TshNetworkLatencyFontSize.Value.ToString();
        if (profile.TshRenderFpsFontSize.HasValue) tshDict["RenderFpsFontSize"] = profile.TshRenderFpsFontSize.Value.ToString();
        if (profile.TshResolutionFontAdjustment.HasValue) tshDict["ResolutionFontAdjustment"] = profile.TshResolutionFontAdjustment.Value.ToString();
        if (profile.TshCursorCaptureEnabledInFullscreenGame.HasValue) tshDict["CursorCaptureEnabledInFullscreenGame"] = BoolToString(profile.TshCursorCaptureEnabledInFullscreenGame.Value);
        if (profile.TshCursorCaptureEnabledInFullscreenMenu.HasValue) tshDict["CursorCaptureEnabledInFullscreenMenu"] = BoolToString(profile.TshCursorCaptureEnabledInFullscreenMenu.Value);
        if (profile.TshCursorCaptureEnabledInWindowedGame.HasValue) tshDict["CursorCaptureEnabledInWindowedGame"] = BoolToString(profile.TshCursorCaptureEnabledInWindowedGame.Value);
        if (profile.TshCursorCaptureEnabledInWindowedMenu.HasValue) tshDict["CursorCaptureEnabledInWindowedMenu"] = BoolToString(profile.TshCursorCaptureEnabledInWindowedMenu.Value);
        if (profile.TshScreenEdgeScrollEnabledInFullscreenApp.HasValue) tshDict["ScreenEdgeScrollEnabledInFullscreenApp"] = BoolToString(profile.TshScreenEdgeScrollEnabledInFullscreenApp.Value);
        if (profile.TshScreenEdgeScrollEnabledInWindowedApp.HasValue) tshDict["ScreenEdgeScrollEnabledInWindowedApp"] = BoolToString(profile.TshScreenEdgeScrollEnabledInWindowedApp.Value);
        if (profile.TshMoneyTransactionVolume.HasValue) tshDict["MoneyTransactionVolume"] = profile.TshMoneyTransactionVolume.Value.ToString();

        if (tshDict.Count > 0)
        {
            options.AdditionalSections["TheSuperHackers"] = tshDict;
        }
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
        profile.VideoSkipEALogo = request.VideoSkipEALogo;
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
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount;
        profile.VideoAntiAliasing = request.VideoAntiAliasing;
        profile.VideoUseLightMap = request.VideoUseLightMap;
        profile.VideoSkipEALogo = request.VideoSkipEALogo;

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
        profile.VideoSkipEALogo = request.VideoSkipEALogo;
    }

    /// <summary>
    /// Patches a GameProfile with non-null values from a CreateProfileRequest.
    /// </summary>
    /// <param name="profile">The GameProfile to patch.</param>
    /// <param name="request">The request containing potentially partial settings.</param>
    public static void PatchGameProfile(GameProfile profile, CreateProfileRequest request)
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

        profile.AudioSoundVolume = request.AudioSoundVolume ?? profile.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume ?? profile.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume ?? profile.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume ?? profile.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled ?? profile.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds ?? profile.AudioNumSounds;

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
        profile.VideoStaticGameLOD = request.VideoStaticGameLOD ?? profile.VideoStaticGameLOD;
        profile.VideoIdealStaticGameLOD = request.VideoIdealStaticGameLOD ?? profile.VideoIdealStaticGameLOD;
        profile.VideoUseDoubleClickAttackMove = request.VideoUseDoubleClickAttackMove ?? profile.VideoUseDoubleClickAttackMove;
        profile.VideoScrollFactor = request.VideoScrollFactor ?? profile.VideoScrollFactor;
        profile.VideoRetaliation = request.VideoRetaliation ?? profile.VideoRetaliation;
        profile.VideoDynamicLOD = request.VideoDynamicLOD ?? profile.VideoDynamicLOD;
        profile.VideoMaxParticleCount = request.VideoMaxParticleCount ?? profile.VideoMaxParticleCount;
        profile.VideoAntiAliasing = request.VideoAntiAliasing ?? profile.VideoAntiAliasing;

        profile.AudioSoundVolume = request.AudioSoundVolume ?? profile.AudioSoundVolume;
        profile.AudioThreeDSoundVolume = request.AudioThreeDSoundVolume ?? profile.AudioThreeDSoundVolume;
        profile.AudioSpeechVolume = request.AudioSpeechVolume ?? profile.AudioSpeechVolume;
        profile.AudioMusicVolume = request.AudioMusicVolume ?? profile.AudioMusicVolume;
        profile.AudioEnabled = request.AudioEnabled ?? profile.AudioEnabled;
        profile.AudioNumSounds = request.AudioNumSounds ?? profile.AudioNumSounds;

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
        target.VideoSkipEALogo = source.VideoSkipEALogo;
    }

    private static bool ParseBool(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value == "1";

    private static string BoolToString(bool value) => value ? "yes" : "no";
}
