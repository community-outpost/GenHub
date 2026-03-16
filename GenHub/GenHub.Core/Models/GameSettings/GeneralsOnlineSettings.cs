namespace GenHub.Core.Models.GameSettings;

/// <summary>GeneralsOnline game client settings (inherits TheSuperHackers settings plus GeneralsOnline-specific options).</summary>
public class GeneralsOnlineSettings : TheSuperHackersSettings
{
    /// <summary>Gets or sets a value indicating whether to show FPS counter.</summary>
    public bool ShowFps { get; set; }

    /// <summary>Gets or sets a value indicating whether to show ping/latency.</summary>
    public bool ShowPing { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to enable auto-login.</summary>
    public bool AutoLogin { get; set; }

    /// <summary>Gets or sets a value indicating whether to remember username.</summary>
    public bool RememberUsername { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to enable notifications.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Gets or sets the chat font size.</summary>
    public int ChatFontSize { get; set; } = 12;

    /// <summary>Gets or sets a value indicating whether to enable sound notifications.</summary>
    public bool EnableSoundNotifications { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to show player ranks.</summary>
    public bool ShowPlayerRanks { get; set; } = true;

    /// <summary>Gets or sets the camera settings.</summary>
    public CameraSettings Camera { get; set; } = new();

    /// <summary>Gets or sets the chat settings.</summary>
    public ChatSettings Chat { get; set; } = new();

    /// <summary>Gets or sets the debug settings.</summary>
    public DebugSettings Debug { get; set; } = new();

    /// <summary>Gets or sets the render settings.</summary>
    public RenderSettings Render { get; set; } = new();

    /// <summary>Gets or sets the social notification settings.</summary>
    public SocialSettings Social { get; set; } = new();

    /// <summary>Nested camera settings.</summary>
    public class CameraSettings
    {
        /// <summary>Gets or sets the maximum camera height only when lobby host.</summary>
        public float MaxHeightOnlyWhenLobbyHost { get; set; } = 310.0f;

        /// <summary>Gets or sets the minimum camera height.</summary>
        public float MinHeight { get; set; } = 310.0f;

        /// <summary>Gets or sets the camera move speed ratio.</summary>
        public float MoveSpeedRatio { get; set; } = 1.5f;
    }

    /// <summary>Nested chat settings.</summary>
    public class ChatSettings
    {
        /// <summary>Gets or sets the chat duration in seconds until fade out.</summary>
        public int DurationSecondsUntilFadeOut { get; set; } = 30;
    }

    /// <summary>Nested debug settings.</summary>
    public class DebugSettings
    {
        /// <summary>Gets or sets a value indicating whether debug verbose logging is enabled.</summary>
        public bool VerboseLogging { get; set; }
    }

    /// <summary>Nested render settings.</summary>
    public class RenderSettings
    {
        /// <summary>Gets or sets the render FPS limit.</summary>
        public int FpsLimit { get; set; } = 144;

        /// <summary>Gets or sets a value indicating whether to limit framerate.</summary>
        public bool LimitFramerate { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to render stats overlay.</summary>
        public bool StatsOverlay { get; set; } = true;
    }

    /// <summary>Nested social settings.</summary>
    public class SocialSettings
    {
        /// <summary>Gets or sets a value indicating whether to show notification when friend comes online in gameplay.</summary>
        public bool NotificationFriendComesOnlineGameplay { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when friend comes online in menus.</summary>
        public bool NotificationFriendComesOnlineMenus { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when friend goes offline in gameplay.</summary>
        public bool NotificationFriendGoesOfflineGameplay { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when friend goes offline in menus.</summary>
        public bool NotificationFriendGoesOfflineMenus { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when player accepts request in gameplay.</summary>
        public bool NotificationPlayerAcceptsRequestGameplay { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when player accepts request in menus.</summary>
        public bool NotificationPlayerAcceptsRequestMenus { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when player sends request in gameplay.</summary>
        public bool NotificationPlayerSendsRequestGameplay { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether to show notification when player sends request in menus.</summary>
        public bool NotificationPlayerSendsRequestMenus { get; set; } = true;
    }
}
