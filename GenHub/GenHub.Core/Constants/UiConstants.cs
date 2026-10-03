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
    public const double DefaultProfileSettingsSidebarWidth = 140;

    /// <summary>
    /// Default fallback width in pixels for profile settings tab sidebars.
    /// </summary>
    public const double DefaultProfileSettingsTabSidebarFallbackWidth = 170;

    /// <summary>
    /// Minimum width for the profile settings sidebar (shows icons only) in pixels.
    /// </summary>
    public const double MinProfileSettingsSidebarWidth = 58;

    /// <summary>
    /// Maximum width for the profile settings sidebar in pixels.
    /// </summary>
    public const double MaxProfileSettingsSidebarWidth = 300;

    /// <summary>
    /// Progressive item render delay in milliseconds for streaming cards into the download browser grid.
    /// </summary>
    public const int ProgressiveItemRenderDelayMs = 20;

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
    /// Color used to indicate inactive or unconfigured status.
    /// </summary>
    public const string StatusInactiveColor = "#888888";

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

    // Content type colors

    /// <summary>Color used for GameClient content type badge and stripe.</summary>
    public const string ContentTypeGameClientColor = "#06B6D4";

    /// <summary>Color used for Mod content type badge and stripe.</summary>
    public const string ContentTypeModColor = "#A855F7";

    /// <summary>Color used for Patch content type badge and stripe.</summary>
    public const string ContentTypePatchColor = "#F59E0B";

    /// <summary>Color used for Map and MapPack content type badge and stripe.</summary>
    public const string ContentTypeMapColor = "#10B981";

    /// <summary>Color used for Addon content type badge and stripe.</summary>
    public const string ContentTypeAddonColor = "#EC4899";

    /// <summary>Color used for ModdingTool and Executable content type badge and stripe.</summary>
    public const string ContentTypeToolColor = "#38BDF8";

    /// <summary>Color used for ContentBundle content type badge and stripe.</summary>
    public const string ContentTypeBundleColor = "#6366F1";

    /// <summary>Color used for Mission content type badge and stripe.</summary>
    public const string ContentTypeMissionColor = "#F97316";

    /// <summary>Color used for Skin and LanguagePack content type badge and stripe.</summary>
    public const string ContentTypeSkinColor = "#8B5CF6";

    /// <summary>
    /// Default subtle background status color for tool status bars.
    /// </summary>
    public const string DefaultStatusBackgroundColor = "Transparent";

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
    /// SVG path data for file browser folder icon.
    /// </summary>
    public const string FileFolderIconPath = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";

    /// <summary>
    /// SVG path data for file browser configuration (gear) icon.
    /// </summary>
    public const string FileConfigIconPath = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

    /// <summary>
    /// SVG path data for file browser image file icon.
    /// </summary>
    public const string FileImageIconPath = "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z";

    /// <summary>
    /// SVG path data for file browser 3D model (cube) icon.
    /// </summary>
    public const string FileModelIconPath = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V9.21L13,12.58V19.29L19,15.91Z";

    /// <summary>
    /// SVG path data for file browser script (terminal) icon.
    /// </summary>
    public const string FileScriptIconPath = "M20,19V7H4V19H20M20,3A2,2 0 0,1 22,5V19A2,2 0 0,1 20,21H4A2,2 0 0,1 2,19V5A2,2 0 0,1 4,3H20M13,17V15H18V17H13M9.58,13L5.5,8.91L6.91,7.5L12.41,13L6.91,18.5L5.5,17.09L9.58,13Z";

    /// <summary>
    /// SVG path data for file browser audio file icon.
    /// </summary>
    public const string FileAudioIconPath = "M21,3V15.5A3.5,3.5 0 0,1 17.5,19A3.5,3.5 0 0,1 14,15.5A3.5,3.5 0 0,1 17.5,12C18.04,12 18.55,12.12 19,12.34V6.47L9,8.6V17.5A3.5,3.5 0 0,1 5.5,21A3.5,3.5 0 0,1 2,17.5A3.5,3.5 0 0,1 5.5,14C6.04,14 6.55,14.12 7,14.34V6L21,3Z";

    /// <summary>
    /// SVG path data for file browser text file icon.
    /// </summary>
    public const string FileTextIconPath = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z";

    /// <summary>
    /// SVG path data for file browser package icon.
    /// </summary>
    public const string FilePackageIconPath = "M12,2L3,7L12,12L21,7L12,2M3,17L12,22L21,17V10.5L12,15.5L3,10.5V17Z";

    /// <summary>
    /// SVG path data for file browser archive icon.
    /// </summary>
    public const string FileArchiveIconPath = "M20,2H4A2,2 0 0,0 2,4V8A2,2 0 0,0 4,10V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V10A2,2 0 0,0 22,8V4A2,2 0 0,0 20,2M9,12H15V14H9V12M20,8H4V4H20V8Z";

    /// <summary>
    /// Default theme color for Generals content.
    /// </summary>
    public const string GeneralsThemeColor = "#BD5A0F";

    /// <summary>
    /// Default theme color for Zero Hour content.
    /// </summary>
    public const string ZeroHourThemeColor = "#1B6575";

    // Converter fallback colors

    /// <summary>Border color for active items.</summary>
    public const string ActiveBorderActiveColor = "#00D9FF";

    /// <summary>Border color for inactive items.</summary>
    public const string ActiveBorderInactiveColor = "#20FFFFFF";

    /// <summary>Highlight color for executable entries.</summary>
    public const string ExecutableHighlightColor = "#90CAF9";

    /// <summary>Brush color for Generals game type indicators.</summary>
    public const string GameTypeGeneralsBrushColor = "#BD5A0F";

    /// <summary>Brush color for Zero Hour game type indicators.</summary>
    public const string GameTypeZeroHourBrushColor = "#2D4963";

    /// <summary>Default color for profile color indicators.</summary>
    public const string ProfileDefaultColor = "#2A2A2A";

    /// <summary>Badge color for game client source types.</summary>
    public const string SourceTypeGameClientBadgeColor = "#FFF9A825";

    /// <summary>Badge color for mod source types.</summary>
    public const string SourceTypeModBadgeColor = "#FF90CAF9";

    /// <summary>Default badge color for other source types.</summary>
    public const string SourceTypeDefaultBadgeColor = "#FFB2FF59";

    /// <summary>Fallback badge color for unknown source types.</summary>
    public const string SourceTypeFallbackBadgeColor = "#FFBDBDBD";

    /// <summary>Fallback color for trusted trust level.</summary>
    public const string TrustLevelTrustedColor = "#10B981";

    /// <summary>Fallback color for verified trust level.</summary>
    public const string TrustLevelVerifiedColor = "#06B6D4";

    /// <summary>Fallback color for untrusted trust level.</summary>
    public const string TrustLevelUntrustedColor = "#9A9AB0";

    /// <summary>Background color for markdown code blocks.</summary>
    public const string MarkdownCodeBlockBackgroundColor = "#1E1E1E";

    /// <summary>Foreground color for markdown code blocks.</summary>
    public const string MarkdownCodeBlockForegroundColor = "#ABB2BF";

    /// <summary>Foreground color for markdown links.</summary>
    public const string MarkdownLinkForegroundColor = "#61AFEF";

    /// <summary>Background color for inline markdown code.</summary>
    public const string MarkdownInlineCodeBackgroundColor = "#2A2A2A";

    /// <summary>Foreground color for inline markdown code.</summary>
    public const string MarkdownInlineCodeForegroundColor = "#E06C75";

    /// <summary>Foreground color for markdown headings.</summary>
    public const string MarkdownHeadingForegroundColor = "#DDDDDD";

    /// <summary>Foreground color for markdown quotes.</summary>
    public const string MarkdownQuoteForegroundColor = "#888888";

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

    // Tab titles and descriptions

    /// <summary>
    /// Title for the Downloads tab.
    /// </summary>
    public const string DownloadsTabTitle = "Downloads";

    /// <summary>
    /// Description for the Downloads tab.
    /// </summary>
    public const string DownloadsTabDescription = "Manage your downloads and installations";

    /// <summary>
    /// Generic loading text displayed during async operations.
    /// </summary>
    public const string LoadingText = "Loading...";
}
