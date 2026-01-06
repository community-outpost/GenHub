using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents a request to create a new game profile.</summary>
public class CreateProfileRequest
{
    /// <summary>Gets or sets the profile name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the profile description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the game installation ID.</summary>
    public string? GameInstallationId { get; set; }

    /// <summary>Gets or sets the game version ID.</summary>
    public string? GameClientId { get; set; }

    /// <summary>
    /// Gets or sets the game client directly. When provided, bypasses the lookup from
    /// AvailableGameClients. Used for provider-based clients (GeneralsOnline, SuperHackers)
    /// where the manifest ID is resolved at runtime.
    /// </summary>
    public GameClient? GameClient { get; set; }

    /// <summary>Gets or sets the preferred workspace strategy.</summary>
    public WorkspaceStrategy PreferredStrategy { get; set; } = WorkspaceConstants.DefaultWorkspaceStrategy;

    /// <summary>Gets or sets the list of enabled content IDs.</summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>Gets or sets the theme color for the profile.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Gets or sets the icon path for the profile.</summary>
    public string? IconPath { get; set; }

    /// <summary>Gets or sets the cover path for the profile.</summary>
    public string? CoverPath { get; set; }

    /// <summary>Gets or sets the command line arguments to pass to the game executable.</summary>
    public string? CommandLineArguments { get; set; }

    /// <summary>Gets or sets the IP address for GameSpy/Networking services.</summary>
    public string? GameSpyIPAddress { get; set; }

    // ===== Video Settings =====

    /// <summary>Gets or sets the video resolution width.</summary>
    public int? VideoResolutionWidth { get; set; }

    /// <summary>Gets or sets the video resolution height.</summary>
    public int? VideoResolutionHeight { get; set; }

    /// <summary>Gets or sets a value indicating whether windowed mode is enabled.</summary>
    public bool? VideoWindowed { get; set; }

    /// <summary>Gets or sets the texture quality.</summary>
    public TextureQuality? VideoTextureQuality { get; set; }

    /// <summary>Gets or sets a value indicating whether shadows are enabled.</summary>
    public bool? EnableVideoShadows { get; set; }

    /// <summary>Gets or sets a value indicating whether particle effects are enabled.</summary>
    public bool? VideoParticleEffects { get; set; }

    /// <summary>Gets or sets a value indicating whether extra animations are enabled.</summary>
    public bool? VideoExtraAnimations { get; set; }

    /// <summary>Gets or sets a value indicating whether building animations are enabled.</summary>
    public bool? VideoBuildingAnimations { get; set; }

    /// <summary>Gets or sets the gamma correction value.</summary>
    public int? VideoGamma { get; set; }

    // ===== Audio Settings =====

    /// <summary>Gets or sets the sound volume.</summary>
    public int? AudioSoundVolume { get; set; }

    /// <summary>Gets or sets the 3D sound volume.</summary>
    public int? AudioThreeDSoundVolume { get; set; }

    /// <summary>Gets or sets the speech volume.</summary>
    public int? AudioSpeechVolume { get; set; }

    /// <summary>Gets or sets the music volume.</summary>
    public int? AudioMusicVolume { get; set; }

    /// <summary>Gets or sets a value indicating whether audio is enabled.</summary>
    public bool? AudioEnabled { get; set; }

    /// <summary>Gets or sets the number of sounds.</summary>
    public int? AudioNumSounds { get; set; }

    // ===== TheSuperHackers Settings =====

    /// <summary>Gets or sets a value indicating whether to archive replays (TSH).</summary>
    public bool? TshArchiveReplays { get; set; }

    /// <summary>Gets or sets a value indicating whether to show money per minute (TSH).</summary>
    public bool? TshShowMoneyPerMinute { get; set; }

    /// <summary>Gets or sets a value indicating whether player observer is enabled (TSH).</summary>
    public bool? TshPlayerObserverEnabled { get; set; }

    /// <summary>Gets or sets the system time font size (TSH).</summary>
    public int? TshSystemTimeFontSize { get; set; }

    /// <summary>Gets or sets the network latency font size (TSH).</summary>
    public int? TshNetworkLatencyFontSize { get; set; }

    /// <summary>Gets or sets the render FPS font size (TSH).</summary>
    public int? TshRenderFpsFontSize { get; set; }

    /// <summary>Gets or sets the resolution font adjustment (TSH).</summary>
    public int? TshResolutionFontAdjustment { get; set; }

    /// <summary>Gets or sets the cursor capture in fullscreen game (TSH).</summary>
    public bool? TshCursorCaptureEnabledInFullscreenGame { get; set; }

    /// <summary>Gets or sets the cursor capture in fullscreen menu (TSH).</summary>
    public bool? TshCursorCaptureEnabledInFullscreenMenu { get; set; }

    /// <summary>Gets or sets the cursor capture in windowed game (TSH).</summary>
    public bool? TshCursorCaptureEnabledInWindowedGame { get; set; }

    /// <summary>Gets or sets the cursor capture in windowed menu (TSH).</summary>
    public bool? TshCursorCaptureEnabledInWindowedMenu { get; set; }

    /// <summary>Gets or sets the screen edge scroll in fullscreen app (TSH).</summary>
    public bool? TshScreenEdgeScrollEnabledInFullscreenApp { get; set; }

    /// <summary>Gets or sets the screen edge scroll in windowed app (TSH).</summary>
    public bool? TshScreenEdgeScrollEnabledInWindowedApp { get; set; }

    /// <summary>Gets or sets the money transaction volume (TSH).</summary>
    public int? TshMoneyTransactionVolume { get; set; }

    // ===== GeneralsOnline Settings =====

    /// <summary>Gets or sets a value indicating whether to show FPS (GO).</summary>
    public bool? GoShowFps { get; set; }

    /// <summary>Gets or sets a value indicating whether to show ping (GO).</summary>
    public bool? GoShowPing { get; set; }

    /// <summary>Gets or sets a value indicating whether to show player ranks (GO).</summary>
    public bool? GoShowPlayerRanks { get; set; }

    /// <summary>Gets or sets a value indicating whether to auto login (GO).</summary>
    public bool? GoAutoLogin { get; set; }

    /// <summary>Gets or sets a value indicating whether to remember username (GO).</summary>
    public bool? GoRememberUsername { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable notifications (GO).</summary>
    public bool? GoEnableNotifications { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable sound notifications (GO).</summary>
    public bool? GoEnableSoundNotifications { get; set; }

    /// <summary>Gets or sets the chat font size (GO).</summary>
    public int? GoChatFontSize { get; set; }

    // ===== Camera Settings =====

    /// <summary>Gets or sets the camera max height (GO).</summary>
    public float? GoCameraMaxHeightOnlyWhenLobbyHost { get; set; }

    /// <summary>Gets or sets the camera min height (GO).</summary>
    public float? GoCameraMinHeight { get; set; }

    /// <summary>Gets or sets the camera move speed ratio (GO).</summary>
    public float? GoCameraMoveSpeedRatio { get; set; }

    // ===== Chat Settings =====

    /// <summary>Gets or sets the chat duration until fade (GO).</summary>
    public int? GoChatDurationSecondsUntilFadeOut { get; set; }

    // ===== Debug Settings =====

    /// <summary>Gets or sets a value indicating whether verbose logging is enabled (GO).</summary>
    public bool? GoDebugVerboseLogging { get; set; }

    // ===== Render Settings =====

    /// <summary>Gets or sets the render FPS limit (GO).</summary>
    public int? GoRenderFpsLimit { get; set; }

    /// <summary>Gets or sets a value indicating whether to limit framerate (GO).</summary>
    public bool? GoRenderLimitFramerate { get; set; }

    /// <summary>Gets or sets a value indicating whether to show stats overlay (GO).</summary>
    public bool? GoRenderStatsOverlay { get; set; }

    /// <summary>Gets or sets the social notification friend online gameplay (GO).</summary>
    public bool? GoSocialNotificationFriendComesOnlineGameplay { get; set; }

    /// <summary>Gets or sets the social notification friend online menus (GO).</summary>
    public bool? GoSocialNotificationFriendComesOnlineMenus { get; set; }

    /// <summary>Gets or sets the social notification friend offline gameplay (GO).</summary>
    public bool? GoSocialNotificationFriendGoesOfflineGameplay { get; set; }

    /// <summary>Gets or sets the social notification friend offline menus (GO).</summary>
    public bool? GoSocialNotificationFriendGoesOfflineMenus { get; set; }

    /// <summary>Gets or sets the social notification player accepts request gameplay (GO).</summary>
    public bool? GoSocialNotificationPlayerAcceptsRequestGameplay { get; set; }

    /// <summary>Gets or sets the social notification player accepts request menus (GO).</summary>
    public bool? GoSocialNotificationPlayerAcceptsRequestMenus { get; set; }

    /// <summary>Gets or sets the social notification player sends request gameplay (GO).</summary>
    public bool? GoSocialNotificationPlayerSendsRequestGameplay { get; set; }

    /// <summary>Gets or sets the social notification player sends request menus (GO).</summary>
    public bool? GoSocialNotificationPlayerSendsRequestMenus { get; set; }
}
