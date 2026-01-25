namespace GenHub.Core.Constants;

/// <summary>
/// UI-related constants for consistent user experience.
/// </summary>
public static class UiConstants
{
    /// <summary>
    /// Default main window width in pixels.
    /// </summary>
    public const double DefaultWindowWidth = 1200;

    /// <summary>
    /// Default main window height in pixels.
    /// </summary>
    public const double DefaultWindowHeight = 800;

    /// <summary>
    /// Default width for GameProfileSettingsWindow in pixels.
    /// </summary>
    public const double DefaultProfileSettingsWidth = 750;

    /// <summary>
    /// Default height for GameProfileSettingsWindow in pixels.
    /// </summary>
    public const double DefaultProfileSettingsHeight = 700;

    // Status colors

    /// <summary>
    /// Color used to indicate success or positive status.
    /// </summary>
    public const string StatusSuccessColor = "#4CAF50";

    /// <summary>
    /// Color used to indicate error or negative status.
    /// </summary>
    public const string StatusErrorColor = "#F44336";

    // Content type display names

    /// <summary>
    /// Display name for Game Client content type.
    /// </summary>
    public const string GameClientDisplayName = "Game Clients";

    /// <summary>
    /// Display name for Map Pack content type.
    /// </summary>
    public const string MapPackDisplayName = "Map Packs";

    /// <summary>
    /// Display name for Patch content type.
    /// </summary>
    public const string PatchDisplayName = "Patches";

    /// <summary>
    /// Display name for Addon content type.
    /// </summary>
    public const string AddonDisplayName = "Addons";

    /// <summary>
    /// Display name for Mod content type.
    /// </summary>
    public const string ModDisplayName = "Mods";

    /// <summary>
    /// Display name for Mission content type.
    /// </summary>
    public const string MissionDisplayName = "Missions";

    /// <summary>
    /// Display name for Map content type.
    /// </summary>
    public const string MapDisplayName = "Maps";

    /// <summary>
    /// Display name for Language Pack content type.
    /// </summary>
    public const string LanguagePackDisplayName = "Language Packs";

    /// <summary>
    /// Display name for Content Bundle content type.
    /// </summary>
    public const string ContentBundleDisplayName = "Bundles";

    // Error Messages

    /// <summary>
    /// Generic error message for failed service status loading.
    /// </summary>
    public const string FailedToLoadServiceStatus = "Failed to load service status. Please try again.";

    /// <summary>
    /// Generic error message for failed player data loading.
    /// </summary>
    public const string FailedToLoadPlayerData = "Failed to load player data. Please try again.";

    /// <summary>
    /// Generic error message for failed leaderboard loading.
    /// </summary>
    public const string FailedToLoadLeaderboard = "Failed to load leaderboard data. Please try again.";

    /// <summary>
    /// Generic error message for failed match history loading.
    /// </summary>
    public const string FailedToLoadMatchHistory = "Failed to load match history. Please try again.";
}
