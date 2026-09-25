using GenHub.Core.Constants;
using GenHub.Core.Models.GameProfile;
using System;
using System.Linq;

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
               HasCustomCameraSettings(profile) ||
               HasCustomNetworkSettings(profile);
    }

    /// <summary>
    /// Checks if a profile runs the GeneralsOnline client.
    /// </summary>
    /// <remarks>
    /// A recorded publisher type settles the question either way. The client name and the enabled
    /// content ids are consulted only when no publisher type was recorded, which is the case for
    /// profiles created before it existed: a TheSuperHackers profile with GeneralsOnline content
    /// enabled belongs to TheSuperHackers, and answering otherwise would let it rewrite the
    /// GeneralsOnline client's global settings.
    /// </remarks>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if the profile runs GeneralsOnline, false otherwise.</returns>
    public static bool IsGeneralsOnlineProfile(this GameProfile profile)
    {
        var publisherType = profile.GameClient?.PublisherType;
        if (!string.IsNullOrWhiteSpace(publisherType))
        {
            return string.Equals(publisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase);
        }

        if (profile.GameClient?.Name?.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return profile.EnabledContentIds?
            .Any(id => id.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// Checks if a profile runs a Community Outpost or Community Patch client.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if the profile runs Community Outpost or Community Patch, false otherwise.</returns>
    public static bool IsCommunityOutpostProfile(this GameProfile profile)
    {
        // Check Community Patch identifiers first so legacy or mislabeled publisher types do not cause a false negative
        if (CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.GameClient?.Name) ||
            CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.GameClient?.Id) ||
            CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.Name) ||
            profile.GameClient?.Name?.Contains(CommunityOutpostConstants.PublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var publisherType = profile.GameClient?.PublisherType;
        if (!string.IsNullOrWhiteSpace(publisherType))
        {
            return string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
        }

        // Only inspect enabled content if it represents a game client or patch manifest, not just an addon/map
        if (profile.EnabledContentIds?
            .Any(id => (id.Contains(ManifestConstants.GameClientManifestSegment, StringComparison.OrdinalIgnoreCase) ||
                        id.Contains(ManifestConstants.PatchManifestSegment, StringComparison.OrdinalIgnoreCase)) &&
                       (id.Contains(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                        CommunityOutpostConstants.IsCommunityPatchIdentifier(id))) == true)
        {
            return true;
        }

        if (profile.GameClient == null && CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.Name))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a profile runs the TheSuperHackers client.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if the profile runs TheSuperHackers, false otherwise.</returns>
    public static bool IsTheSuperHackersProfile(this GameProfile profile)
    {
        if (profile.IsCommunityOutpostProfile())
        {
            return false;
        }

        var publisherType = profile.GameClient?.PublisherType;
        if (!string.IsNullOrWhiteSpace(publisherType))
        {
            return string.Equals(publisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(publisherType, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase);
        }

        if (CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.GameClient?.Name) ||
            CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.GameClient?.Id) ||
            CommunityOutpostConstants.IsCommunityPatchIdentifier(profile.Name))
        {
            return false;
        }

        if (profile.GameClient?.Name?.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true ||
            profile.GameClient?.Name?.Contains(SuperHackersConstants.NameMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return profile.EnabledContentIds?
            .Any(id => id.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) &&
                       !CommunityOutpostConstants.IsCommunityPatchIdentifier(id)) == true;
    }

    /// <summary>
    /// Checks if a profile has custom camera settings configured for non-GeneralsOnline profiles.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if any camera property is set and differs from the default, false otherwise.</returns>
    public static bool HasCustomCameraSettings(this GameProfile profile)
    {
        if (profile.IsGeneralsOnlineProfile())
        {
            return false;
        }

        return (profile.CameraHeight.HasValue && Math.Abs(profile.CameraHeight.Value - GameSettingsConstants.Camera.DefaultHeight) > 0.01f) ||
               (profile.CameraMaxHeight.HasValue && Math.Abs(profile.CameraMaxHeight.Value - GameSettingsConstants.Camera.DefaultMaxHeight) > 0.01f) ||
               (profile.CameraMinHeight.HasValue && Math.Abs(profile.CameraMinHeight.Value - GameSettingsConstants.Camera.DefaultMinHeight) > 0.01f) ||
               (profile.CameraPitch.HasValue && Math.Abs(profile.CameraPitch.Value - GameSettingsConstants.Camera.DefaultPitch) > 0.01f);
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
               profile.TshMoneyTransactionVolume.HasValue ||
               profile.TshGameWindowTransitionSpeedMultiplier.HasValue;
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
