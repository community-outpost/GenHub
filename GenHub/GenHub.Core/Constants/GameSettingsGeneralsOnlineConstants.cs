namespace GenHub.Core.Constants;

/// <summary>
/// GeneralsOnline game client settings constants.
/// </summary>
public static class GameSettingsGeneralsOnlineConstants
{
    /// <summary>
    /// Settings file name for GeneralsOnline.
    /// </summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// Extension of the file a save is written to before it is moved over settings.json.
    /// </summary>
    public const string TemporarySettingsFileExtension = ".tmp";

    /// <summary>
    /// Number of times the completed settings file is moved over settings.json before the save
    /// gives up and reports the failure. Anything holding settings.json open releases it within
    /// milliseconds, so a handful of attempts either succeeds or is looking at a real fault.
    /// </summary>
    public const int SettingsReplaceAttemptLimit = 5;

    /// <summary>
    /// Delay between attempts to move the completed settings file over settings.json.
    /// </summary>
    public const int SettingsReplaceRetryDelayMilliseconds = 20;

    /// <summary>
    /// Default chat font size.
    /// </summary>
    public const int DefaultChatFontSize = 12;

    /// <summary>
    /// Minimum chat font size.
    /// </summary>
    public const int MinChatFontSize = 8;

    /// <summary>
    /// Maximum chat font size.
    /// </summary>
    public const int MaxChatFontSize = 24;

    /// <summary>
    /// Default for whether ping is shown.
    /// </summary>
    public const bool DefaultShowPing = true;

    /// <summary>
    /// Default for whether player ranks are shown.
    /// </summary>
    public const bool DefaultShowPlayerRanks = true;

    /// <summary>
    /// Default for whether the username is remembered.
    /// </summary>
    public const bool DefaultRememberUsername = true;

    /// <summary>
    /// Default for whether notifications are enabled.
    /// </summary>
    public const bool DefaultEnableNotifications = true;

    /// <summary>
    /// Default for whether sound notifications are enabled.
    /// </summary>
    public const bool DefaultEnableSoundNotifications = true;
}
