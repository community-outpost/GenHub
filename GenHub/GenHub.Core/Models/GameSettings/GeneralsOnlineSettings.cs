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
}
