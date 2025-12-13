using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a request to update a game profile.
/// </summary>
public class UpdateProfileRequest
{
    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the profile description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of enabled content IDs.
    /// </summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>
    /// Gets or sets the preferred workspace strategy.
    /// </summary>
    public WorkspaceStrategy? PreferredStrategy { get; set; }

    /// <summary>
    /// Gets or sets the launch arguments.
    /// </summary>
    public Dictionary<string, string>? LaunchArguments { get; set; }

    /// <summary>
    /// Gets or sets the environment variables.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the custom executable path.
    /// </summary>
    public string? CustomExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the active workspace ID.
    /// </summary>
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }

    /// <summary>
    /// Gets or sets the game installation ID.
    /// </summary>
    public string? GameInstallationId { get; set; }

    /// <summary>
    /// Gets or sets the command line arguments to pass to the game executable.
    /// </summary>
    public string? CommandLineArguments { get; set; }

    /// <summary>
    /// Gets or sets the video resolution width for this profile.
    /// </summary>
    public int? VideoResolutionWidth { get; set; }

    /// <summary>
    /// Gets or sets the video resolution height for this profile.
    /// </summary>
    public int? VideoResolutionHeight { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this profile runs in windowed mode.
    /// </summary>
    public bool? VideoWindowed { get; set; }

    /// <summary>
    /// Gets or sets the texture quality for this profile.
    /// </summary>
    public TextureQuality? VideoTextureQuality { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether video shadows are enabled for this profile.
    /// </summary>
    public bool? EnableVideoShadows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether particle effects are enabled for this profile.
    /// </summary>
    public bool? VideoParticleEffects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether extra animations are enabled for this profile.
    /// </summary>
    public bool? VideoExtraAnimations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether building animations are enabled for this profile.
    /// </summary>
    public bool? VideoBuildingAnimations { get; set; }

    /// <summary>
    /// Gets or sets the gamma correction value for this profile (0-100).
    /// This maps directly to the in-game gamma setting range.
    /// </summary>
    public int? VideoGamma { get; set; }

    /// <summary>
    /// Gets or sets the sound volume for this profile.
    /// </summary>
    public int? AudioSoundVolume { get; set; }

    /// <summary>
    /// Gets or sets the 3D sound volume for this profile.
    /// </summary>
    public int? AudioThreeDSoundVolume { get; set; }

    /// <summary>
    /// Gets or sets the speech volume for this profile.
    /// </summary>
    public int? AudioSpeechVolume { get; set; }

    /// <summary>
    /// Gets or sets the music volume for this profile.
    /// </summary>
    public int? AudioMusicVolume { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether audio is enabled for this profile.
    /// </summary>
    public bool? AudioEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of sounds for this profile.
    /// </summary>
    public int? AudioNumSounds { get; set; }

    // ===== TheSuperHackers Client Settings =====

    /// <summary>Gets or sets a value indicating whether to archive replays automatically (TSH).</summary>
    public bool? TshArchiveReplays { get; set; }

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in fullscreen game (TSH).</summary>
    public bool? TshCursorCaptureEnabledInFullscreenGame { get; set; }

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in fullscreen menu (TSH).</summary>
    public bool? TshCursorCaptureEnabledInFullscreenMenu { get; set; }

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in windowed game (TSH).</summary>
    public bool? TshCursorCaptureEnabledInWindowedGame { get; set; }

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in windowed menu (TSH).</summary>
    public bool? TshCursorCaptureEnabledInWindowedMenu { get; set; }

    /// <summary>Gets or sets the volume of money transaction audio events (TSH, 0-100).</summary>
    public int? TshMoneyTransactionVolume { get; set; }

    /// <summary>Gets or sets the font size for network latency display (TSH, 0 to disable).</summary>
    public int? TshNetworkLatencyFontSize { get; set; }

    /// <summary>Gets or sets a value indicating whether player observer mode is enabled (TSH).</summary>
    public bool? TshPlayerObserverEnabled { get; set; }

    /// <summary>Gets or sets the font size for FPS display (TSH, 0 to disable).</summary>
    public int? TshRenderFpsFontSize { get; set; }

    /// <summary>Gets or sets the resolution font adjustment (TSH, -100 to 100).</summary>
    public int? TshResolutionFontAdjustment { get; set; }

    /// <summary>Gets or sets a value indicating whether screen edge scrolling is enabled in fullscreen app (TSH).</summary>
    public bool? TshScreenEdgeScrollEnabledInFullscreenApp { get; set; }

    /// <summary>Gets or sets a value indicating whether screen edge scrolling is enabled in windowed app (TSH).</summary>
    public bool? TshScreenEdgeScrollEnabledInWindowedApp { get; set; }

    /// <summary>Gets or sets a value indicating whether to show money per minute (TSH).</summary>
    public bool? TshShowMoneyPerMinute { get; set; }

    /// <summary>Gets or sets the font size for system time display (TSH, 0 to disable).</summary>
    public int? TshSystemTimeFontSize { get; set; }

    // ===== GeneralsOnline Client Settings =====

    /// <summary>Gets or sets a value indicating whether to show FPS counter (GO).</summary>
    public bool? GoShowFps { get; set; }

    /// <summary>Gets or sets a value indicating whether to show ping/latency (GO).</summary>
    public bool? GoShowPing { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable auto-login (GO).</summary>
    public bool? GoAutoLogin { get; set; }

    /// <summary>Gets or sets a value indicating whether to remember username (GO).</summary>
    public bool? GoRememberUsername { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable notifications (GO).</summary>
    public bool? GoEnableNotifications { get; set; }

    /// <summary>Gets or sets the chat font size (GO).</summary>
    public int? GoChatFontSize { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable sound notifications (GO).</summary>
    public bool? GoEnableSoundNotifications { get; set; }

    /// <summary>Gets or sets a value indicating whether to show player ranks (GO).</summary>
    public bool? GoShowPlayerRanks { get; set; }
}
