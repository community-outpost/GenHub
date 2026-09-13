namespace GenHub.Core.Constants;

/// <summary>
/// TheSuperHackers game client settings constants.
/// </summary>
public static class GameSettingsTheSuperHackersConstants
{
    /// <summary>
    /// Section name for TheSuperHackers settings in Options.ini.
    /// </summary>
    public const string SectionName = "TheSuperHackers";

    /// <summary>
    /// Configuration key for use double click attack move.
    /// </summary>
    public const string UseDoubleClickAttackMoveKey = "UseDoubleClickAttackMove";

    /// <summary>
    /// Configuration key for fallback use double click.
    /// </summary>
    public const string UseDoubleClickKey = "UseDoubleClick";

    /// <summary>
    /// Configuration key for scroll factor.
    /// </summary>
    public const string ScrollFactorKey = "ScrollFactor";

    /// <summary>
    /// Configuration key for retaliation.
    /// </summary>
    public const string RetaliationKey = "Retaliation";

    /// <summary>
    /// Configuration key for dynamic LOD.
    /// </summary>
    public const string DynamicLODKey = "DynamicLOD";

    /// <summary>
    /// Configuration key for max particle count.
    /// </summary>
    public const string MaxParticleCountKey = "MaxParticleCount";

    /// <summary>
    /// Configuration key for archive replays.
    /// </summary>
    public const string ArchiveReplaysKey = "ArchiveReplays";

    /// <summary>
    /// Configuration key for show money per minute.
    /// </summary>
    public const string ShowMoneyPerMinuteKey = "ShowMoneyPerMinute";

    /// <summary>
    /// Configuration key for player observer enabled.
    /// </summary>
    public const string PlayerObserverEnabledKey = "PlayerObserverEnabled";

    /// <summary>
    /// Configuration key for system time font size.
    /// </summary>
    public const string SystemTimeFontSizeKey = "SystemTimeFontSize";

    /// <summary>
    /// Configuration key for network latency font size.
    /// </summary>
    public const string NetworkLatencyFontSizeKey = "NetworkLatencyFontSize";

    /// <summary>
    /// Configuration key for render FPS font size.
    /// </summary>
    public const string RenderFpsFontSizeKey = "RenderFpsFontSize";

    /// <summary>
    /// Configuration key for resolution font adjustment.
    /// </summary>
    public const string ResolutionFontAdjustmentKey = "ResolutionFontAdjustment";

    /// <summary>
    /// Configuration key for cursor capture in fullscreen game.
    /// </summary>
    public const string CursorCaptureEnabledInFullscreenGameKey = "CursorCaptureEnabledInFullscreenGame";

    /// <summary>
    /// Configuration key for cursor capture in fullscreen menu.
    /// </summary>
    public const string CursorCaptureEnabledInFullscreenMenuKey = "CursorCaptureEnabledInFullscreenMenu";

    /// <summary>
    /// Configuration key for cursor capture in windowed game.
    /// </summary>
    public const string CursorCaptureEnabledInWindowedGameKey = "CursorCaptureEnabledInWindowedGame";

    /// <summary>
    /// Configuration key for cursor capture in windowed menu.
    /// </summary>
    public const string CursorCaptureEnabledInWindowedMenuKey = "CursorCaptureEnabledInWindowedMenu";

    /// <summary>
    /// Configuration key for screen edge scroll in fullscreen app.
    /// </summary>
    public const string ScreenEdgeScrollEnabledInFullscreenAppKey = "ScreenEdgeScrollEnabledInFullscreenApp";

    /// <summary>
    /// Configuration key for screen edge scroll in windowed app.
    /// </summary>
    public const string ScreenEdgeScrollEnabledInWindowedAppKey = "ScreenEdgeScrollEnabledInWindowedApp";

    /// <summary>
    /// Configuration key for money transaction volume.
    /// </summary>
    public const string MoneyTransactionVolumeKey = "MoneyTransactionVolume";

    /// <summary>
    /// Configuration key for game time font size.
    /// </summary>
    public const string GameTimeFontSizeKey = "GameTimeFontSize";

    /// <summary>
    /// Configuration key for draw scroll anchor.
    /// </summary>
    public const string DrawScrollAnchorKey = "DrawScrollAnchor";

    /// <summary>
    /// Configuration key for move scroll anchor.
    /// </summary>
    public const string MoveScrollAnchorKey = "MoveScrollAnchor";

    /// <summary>
    /// Configuration key for language filter.
    /// </summary>
    public const string LanguageFilterKey = "LanguageFilter";

    /// <summary>
    /// Configuration key for send delay.
    /// </summary>
    public const string SendDelayKey = "SendDelay";

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
    /// Configuration key for game window transition speed multiplier.
    /// </summary>
    public const string GameWindowTransitionSpeedMultiplierKey = "GameWindowTransitionSpeedMultiplier";

    /// <summary>
    /// Minimum game window transition speed multiplier value.
    /// </summary>
    public const float MinGameWindowTransitionSpeedMultiplier = 1.0f;

    /// <summary>
    /// Maximum game window transition speed multiplier value.
    /// </summary>
    public const float MaxGameWindowTransitionSpeedMultiplier = 4.0f;

    /// <summary>
    /// Default game window transition speed multiplier value.
    /// </summary>
    public const float DefaultGameWindowTransitionSpeedMultiplier = 1.0f;
}
