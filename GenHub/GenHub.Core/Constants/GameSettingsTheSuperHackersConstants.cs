namespace GenHub.Core.Constants;

/// <summary>
/// TheSuperHackers game client settings constants.
/// </summary>
public static class GameSettingsTheSuperHackersConstants
{
    /// <summary>
    /// Minimum font size value.
    /// </summary>
    public const int MinFontSize = 0;

    /// <summary>
    /// Maximum font size value.
    /// </summary>
    public const int MaxFontSize = 72;

    /// <summary>
    /// Minimum resolution font adjustment value.
    /// </summary>
    public const int MinResolutionFontAdjustment = -100;

    /// <summary>
    /// Maximum resolution font adjustment value.
    /// </summary>
    public const int MaxResolutionFontAdjustment = 100;

    /// <summary>
    /// Default resolution font adjustment value.
    /// </summary>
    public const int DefaultResolutionFontAdjustment = -100;

    /// <summary>
    /// Default font size for network latency display.
    /// </summary>
    public const int DefaultNetworkLatencyFontSize = 8;

    /// <summary>
    /// Default font size for FPS display.
    /// </summary>
    public const int DefaultRenderFpsFontSize = 8;

    /// <summary>
    /// Default font size for system time display.
    /// </summary>
    public const int DefaultSystemTimeFontSize = 8;

    /// <summary>
    /// Default volume for money transaction audio events, on the same 0-100 scale the settings
    /// screen exposes. Zero would mute them, which is a choice rather than a default.
    /// </summary>
    public const int DefaultMoneyTransactionVolume = 50;

    /// <summary>
    /// Default for whether player observer mode is enabled.
    /// Matches the engine fallback in OptionPreferences::getPlayerObserverEnabled.
    /// </summary>
    public const bool DefaultPlayerObserverEnabled = true;

    /// <summary>
    /// Default for cursor capture in fullscreen game.
    /// Included in the engine's CursorCaptureMode_Default mask.
    /// </summary>
    public const bool DefaultCursorCaptureEnabledInFullscreenGame = true;

    /// <summary>
    /// Default for cursor capture in fullscreen menu.
    /// Included in the engine's CursorCaptureMode_Default mask.
    /// </summary>
    public const bool DefaultCursorCaptureEnabledInFullscreenMenu = true;

    /// <summary>
    /// Default for cursor capture in windowed game.
    /// Included in the engine's CursorCaptureMode_Default mask.
    /// </summary>
    public const bool DefaultCursorCaptureEnabledInWindowedGame = true;

    /// <summary>
    /// Default for cursor capture in windowed menu.
    /// Absent from the engine's CursorCaptureMode_Default mask.
    /// </summary>
    public const bool DefaultCursorCaptureEnabledInWindowedMenu = false;

    /// <summary>
    /// Default for screen edge scrolling in a fullscreen app.
    /// The engine's ScreenEdgeScrollMode_Default is exactly this flag.
    /// </summary>
    public const bool DefaultScreenEdgeScrollEnabledInFullscreenApp = true;

    /// <summary>
    /// Default for screen edge scrolling in a windowed app.
    /// Absent from the engine's ScreenEdgeScrollMode_Default.
    /// </summary>
    public const bool DefaultScreenEdgeScrollEnabledInWindowedApp = false;

    /// <summary>
    /// Minimum game window transition speed multiplier value.
    /// </summary>
    public const float MinGameWindowTransitionSpeedMultiplier = 1.0f;

    /// <summary>
    /// Maximum game window transition speed multiplier value.
    /// </summary>
    public const float MaxGameWindowTransitionSpeedMultiplier = 1000.0f;

    /// <summary>
    /// Default game window transition speed multiplier value.
    /// </summary>
    public const float DefaultGameWindowTransitionSpeedMultiplier = 1.0f;
}
