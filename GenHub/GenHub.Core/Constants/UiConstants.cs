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

    /// <summary>
    /// Default width for the profile settings sidebar in pixels.
    /// </summary>
    public const double DefaultProfileSettingsSidebarWidth = 190;

    /// <summary>
    /// Minimum width for the profile settings sidebar (shows icons only) in pixels.
    /// </summary>
    public const double MinProfileSettingsSidebarWidth = 68;

    /// <summary>
    /// Maximum width for the profile settings sidebar in pixels.
    /// </summary>
    public const double MaxProfileSettingsSidebarWidth = 300;

    // Status colors

    /// <summary>
    /// Color used to indicate success or positive status.
    /// </summary>
    public const string StatusSuccessColor = "#4CAF50";

    /// <summary>
    /// Color used to indicate error or negative status.
    /// </summary>
    public const string StatusErrorColor = "#F44336";

    /// <summary>
    /// Color used for downloaded status indicator.
    /// </summary>
    public const string StatusDownloadedColor = "#4CAF50";

    /// <summary>
    /// Color used for not downloaded status indicator.
    /// </summary>
    public const string StatusNotDownloadedColor = "#B388FF";

    /// <summary>
    /// Color used for update available status indicator.
    /// </summary>
    public const string StatusUpdateAvailableColor = "#FFB74D";

    /// <summary>
    /// Color used for update failed status indicator.
    /// </summary>
    public const string StatusUpdateFailedColor = "#F44336";

    /// <summary>
    /// Color used for selected card borders.
    /// </summary>
    public const string CardSelectedBorderColor = "#AB47BC";

    /// <summary>
    /// Color used for selected card backgrounds (semi-transparent purple).
    /// </summary>
    public const string CardSelectedBackgroundColor = "#3CAB47BC";

    /// <summary>
    /// Color used for unselected card backgrounds.
    /// </summary>
    public const string CardUnselectedBackgroundColor = "#252525";

    /// <summary>
    /// SVG path data for transparent checkmark icon.
    /// </summary>
    public const string TransparentCheckmarkIconPath = "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";

    /// <summary>
    /// SVG path data for detailed download arrow icon into tray.
    /// </summary>
    public const string DownloadArrowIconPath = "M5 20h14v-2H5v2zM19 9h-4V3H9v6H5l7 7 7-7z";

    /// <summary>
    /// SVG path data for update sync icon.
    /// </summary>
    public const string UpdateSyncIconPath = "M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0 0 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 0 0 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z";

    /// <summary>
    /// Default theme color for Generals content.
    /// </summary>
    public const string GeneralsThemeColor = "#BD5A0F";

    /// <summary>
    /// Default theme color for Zero Hour content.
    /// </summary>
    public const string ZeroHourThemeColor = "#1B6575";

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

    /// <summary>
    /// Display name for Modding Tool content type.
    /// </summary>
    public const string ModdingToolDisplayName = "Tools";

    /// <summary>
    /// Maximum allowed length of HTML input strings to prevent regex denial of service.
    /// </summary>
    public const int MaxHtmlInputLength = 1_000_000;
}
