namespace GenHub.Core.Models.GameSettings;

/// <summary>TheSuperHackers game client settings from Options.ini.</summary>
public class TheSuperHackersSettings
{
    /// <summary>Gets or sets a value indicating whether to archive replays automatically.</summary>
    public bool ArchiveReplays { get; set; }

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in fullscreen game.</summary>
    public bool CursorCaptureEnabledInFullscreenGame { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in fullscreen menu.</summary>
    public bool CursorCaptureEnabledInFullscreenMenu { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in windowed game.</summary>
    public bool CursorCaptureEnabledInWindowedGame { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether cursor capture is enabled in windowed menu.</summary>
    public bool CursorCaptureEnabledInWindowedMenu { get; set; }

    /// <summary>Gets or sets the volume of money transaction audio events (0-100, 0 to mute).</summary>
    public int MoneyTransactionVolume { get; set; }

    /// <summary>Gets or sets the font size for network latency display (0 to disable).</summary>
    public int NetworkLatencyFontSize { get; set; } = 8;

    /// <summary>Gets or sets a value indicating whether player observer mode is enabled.</summary>
    public bool PlayerObserverEnabled { get; set; } = true;

    /// <summary>Gets or sets the font size for FPS display (0 to disable).</summary>
    public int RenderFpsFontSize { get; set; } = 8;

    /// <summary>Gets or sets the resolution font adjustment (-100 to 100).</summary>
    public int ResolutionFontAdjustment { get; set; } = -100;

    /// <summary>Gets or sets a value indicating whether screen edge scrolling is enabled in fullscreen app.</summary>
    public bool ScreenEdgeScrollEnabledInFullscreenApp { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether screen edge scrolling is enabled in windowed app.</summary>
    public bool ScreenEdgeScrollEnabledInWindowedApp { get; set; }

    /// <summary>Gets or sets a value indicating whether to show money per minute.</summary>
    public bool ShowMoneyPerMinute { get; set; }

    /// <summary>Gets or sets the font size for system time display (0 to disable).</summary>
    public int SystemTimeFontSize { get; set; } = 8;
}
