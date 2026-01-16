using GenHub.Core.Constants;

namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Immutable record representing the game settings for Generals Online.
/// Replaces the legacy GeneralsOnlineSettings class.
/// </summary>
public record GameSettings
{
    // GeneralsOnline specific settings

    /// <summary>Gets a value indicating whether to show FPS.</summary>
    public bool ShowFps { get; init; }

    /// <summary>Gets a value indicating whether to show ping.</summary>
    public bool ShowPing { get; init; } = true;

    /// <summary>Gets a value indicating whether to enable auto-login.</summary>
    public bool AutoLogin { get; init; }

    /// <summary>Gets a value indicating whether to remember the username.</summary>
    public bool RememberUsername { get; init; } = true;

    /// <summary>Gets a value indicating whether to enable notifications.</summary>
    public bool EnableNotifications { get; init; } = true;

    /// <summary>Gets the chat font size.</summary>
    public int ChatFontSize { get; init; } = GameSettingsGeneralsOnlineConstants.DefaultChatFontSize;

    /// <summary>Gets a value indicating whether to enable sound notifications.</summary>
    public bool EnableSoundNotifications { get; init; } = true;

    /// <summary>Gets a value indicating whether to show player ranks.</summary>
    public bool ShowPlayerRanks { get; init; } = true;

    /// <summary>Gets the camera max height, only applied when lobby host.</summary>
    public float CameraMaxHeightOnlyWhenLobbyHost { get; init; } = 310.0f;

    /// <summary>Gets the camera minimum height.</summary>
    public float CameraMinHeight { get; init; } = 310.0f;

    /// <summary>Gets the camera move speed ratio.</summary>
    public float CameraMoveSpeedRatio { get; init; } = 1.5f;

    /// <summary>Gets the chat duration in seconds before fading out.</summary>
    public int ChatDurationSecondsUntilFadeOut { get; init; } = 30;

    /// <summary>Gets a value indicating whether verbose debug logging is enabled.</summary>
    public bool DebugVerboseLogging { get; init; }

    /// <summary>Gets the render FPS limit.</summary>
    public int RenderFpsLimit { get; init; } = 144;

    /// <summary>Gets a value indicating whether to limit the render framerate.</summary>
    public bool RenderLimitFramerate { get; init; } = true;

    /// <summary>Gets a value indicating whether to show the render stats overlay.</summary>
    public bool RenderStatsOverlay { get; init; } = true;

    // Social Notifications

    /// <summary>Gets a value indicating whether to show online friend notifications during gameplay.</summary>
    public bool SocialNotificationFriendComesOnlineGameplay { get; init; } = true;

    /// <summary>Gets a value indicating whether to show online friend notifications in menus.</summary>
    public bool SocialNotificationFriendComesOnlineMenus { get; init; } = true;

    /// <summary>Gets a value indicating whether to show offline friend notifications during gameplay.</summary>
    public bool SocialNotificationFriendGoesOfflineGameplay { get; init; } = true;

    /// <summary>Gets a value indicating whether to show offline friend notifications in menus.</summary>
    public bool SocialNotificationFriendGoesOfflineMenus { get; init; } = true;

    /// <summary>Gets a value indicating whether to show accepted request notifications during gameplay.</summary>
    public bool SocialNotificationPlayerAcceptsRequestGameplay { get; init; } = true;

    /// <summary>Gets a value indicating whether to show accepted request notifications in menus.</summary>
    public bool SocialNotificationPlayerAcceptsRequestMenus { get; init; } = true;

    /// <summary>Gets a value indicating whether to show sent request notifications during gameplay.</summary>
    public bool SocialNotificationPlayerSendsRequestGameplay { get; init; } = true;

    /// <summary>Gets a value indicating whether to show sent request notifications in menus.</summary>
    public bool SocialNotificationPlayerSendsRequestMenus { get; init; } = true;

    // Properties inherited from TheSuperHackersSettings (now explicitly defined in this record)

    /// <summary>Gets a value indicating whether to archive replays.</summary>
    public bool ArchiveReplays { get; init; }

    /// <summary>Gets a value indicating whether cursor capture is enabled in fullscreen game.</summary>
    public bool CursorCaptureEnabledInFullscreenGame { get; init; } = true;

    /// <summary>Gets a value indicating whether cursor capture is enabled in fullscreen menu.</summary>
    public bool CursorCaptureEnabledInFullscreenMenu { get; init; } = true;

    /// <summary>Gets a value indicating whether cursor capture is enabled in windowed game.</summary>
    public bool CursorCaptureEnabledInWindowedGame { get; init; } = true;

    /// <summary>Gets a value indicating whether cursor capture is enabled in windowed menu.</summary>
    public bool CursorCaptureEnabledInWindowedMenu { get; init; }

    /// <summary>Gets the money transaction volume.</summary>
    public int MoneyTransactionVolume { get; init; }

    /// <summary>Gets the network latency font size.</summary>
    public int NetworkLatencyFontSize { get; init; } = 8;

    /// <summary>Gets a value indicating whether the player observer is enabled.</summary>
    public bool PlayerObserverEnabled { get; init; } = true;

    /// <summary>Gets the render FPS font size.</summary>
    public int RenderFpsFontSize { get; init; } = 8;

    /// <summary>Gets the resolution font adjustment.</summary>
    public int ResolutionFontAdjustment { get; init; } = -100;

    /// <summary>Gets a value indicating whether screen edge scroll is enabled in fullscreen app.</summary>
    public bool ScreenEdgeScrollEnabledInFullscreenApp { get; init; } = true;

    /// <summary>Gets a value indicating whether screen edge scroll is enabled in windowed app.</summary>
    public bool ScreenEdgeScrollEnabledInWindowedApp { get; init; }

    /// <summary>Gets a value indicating whether to show money per minute.</summary>
    public bool ShowMoneyPerMinute { get; init; }

    /// <summary>Gets the system time font size.</summary>
    public int SystemTimeFontSize { get; init; } = 8;
}
