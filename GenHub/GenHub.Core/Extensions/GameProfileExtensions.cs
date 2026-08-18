using GenHub.Core.Models.GameProfile;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for GameProfile.
/// </summary>
public static class GameProfileExtensions
{
    /// <summary>
    /// Checks if a profile has any custom game settings defined.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if the profile has custom settings, false otherwise.</returns>
    public static bool HasCustomSettings(this GameProfile profile)
    {
        return HasCustomVideoSettings(profile) ||
               HasCustomAudioSettings(profile) ||
               HasCustomTshSettings(profile) ||
               HasCustomGeneralsOnlineSettings(profile) ||
               HasCustomNetworkSettings(profile);
    }

    private static bool HasCustomVideoSettings(GameProfile profile)
    {
        return profile.VideoResolutionWidth.HasValue ||
               profile.VideoResolutionHeight.HasValue ||
               profile.VideoWindowed.HasValue ||
               profile.VideoTextureQuality.HasValue ||
               profile.EnableVideoShadows.HasValue ||
               profile.VideoParticleEffects.HasValue ||
               profile.VideoExtraAnimations.HasValue ||
               profile.VideoBuildingAnimations.HasValue ||
               profile.VideoGamma.HasValue ||
               profile.VideoAlternateMouseSetup.HasValue ||
               profile.VideoStaticGameLOD != null ||
               profile.VideoIdealStaticGameLOD != null ||
               profile.VideoUseDoubleClickAttackMove.HasValue ||
               profile.VideoScrollFactor.HasValue ||
               profile.VideoRetaliation.HasValue ||
               profile.VideoDynamicLOD.HasValue ||
               profile.VideoMaxParticleCount.HasValue ||
               profile.VideoAntiAliasing.HasValue;
    }

    private static bool HasCustomAudioSettings(GameProfile profile)
    {
        return profile.AudioSoundVolume.HasValue ||
               profile.AudioThreeDSoundVolume.HasValue ||
               profile.AudioSpeechVolume.HasValue ||
               profile.AudioMusicVolume.HasValue ||
               profile.AudioEnabled.HasValue ||
               profile.AudioNumSounds.HasValue;
    }

    private static bool HasCustomTshSettings(GameProfile profile)
    {
        return profile.TshArchiveReplays.HasValue ||
               profile.TshShowMoneyPerMinute.HasValue ||
               profile.TshPlayerObserverEnabled.HasValue ||
               profile.TshSystemTimeFontSize.HasValue ||
               profile.TshNetworkLatencyFontSize.HasValue ||
               profile.TshRenderFpsFontSize.HasValue ||
               profile.TshResolutionFontAdjustment.HasValue ||
               profile.TshCursorCaptureEnabledInFullscreenGame.HasValue ||
               profile.TshCursorCaptureEnabledInFullscreenMenu.HasValue ||
               profile.TshCursorCaptureEnabledInWindowedGame.HasValue ||
               profile.TshCursorCaptureEnabledInWindowedMenu.HasValue ||
               profile.TshScreenEdgeScrollEnabledInFullscreenApp.HasValue ||
               profile.TshScreenEdgeScrollEnabledInWindowedApp.HasValue ||
               profile.TshMoneyTransactionVolume.HasValue;
    }

    private static bool HasCustomGeneralsOnlineSettings(GameProfile profile)
    {
        return profile.GoShowFps.HasValue ||
               profile.GoShowPing.HasValue ||
               profile.GoAutoLogin.HasValue ||
               profile.GoRememberUsername.HasValue ||
               profile.GoEnableNotifications.HasValue ||
               profile.GoChatFontSize.HasValue ||
               profile.GoEnableSoundNotifications.HasValue ||
               profile.GoShowPlayerRanks.HasValue ||
               profile.GoCameraMaxHeightOnlyWhenLobbyHost.HasValue ||
               profile.GoCameraMinHeight.HasValue ||
               profile.GoCameraMoveSpeedRatio.HasValue ||
               profile.GoChatDurationSecondsUntilFadeOut.HasValue ||
               profile.GoDebugVerboseLogging.HasValue ||
               profile.GoRenderFpsLimit.HasValue ||
               profile.GoRenderLimitFramerate.HasValue ||
               profile.GoRenderStatsOverlay.HasValue ||
               profile.GoSocialNotificationFriendComesOnlineGameplay.HasValue ||
               profile.GoSocialNotificationFriendComesOnlineMenus.HasValue ||
               profile.GoSocialNotificationFriendGoesOfflineGameplay.HasValue ||
               profile.GoSocialNotificationFriendGoesOfflineMenus.HasValue ||
               profile.GoSocialNotificationPlayerAcceptsRequestGameplay.HasValue ||
               profile.GoSocialNotificationPlayerAcceptsRequestMenus.HasValue ||
               profile.GoSocialNotificationPlayerSendsRequestGameplay.HasValue ||
               profile.GoSocialNotificationPlayerSendsRequestMenus.HasValue;
    }

    private static bool HasCustomNetworkSettings(GameProfile profile)
    {
        return !string.IsNullOrEmpty(profile.GameSpyIPAddress);
    }
}
