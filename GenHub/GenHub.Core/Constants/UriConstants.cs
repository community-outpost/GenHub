using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// URI scheme constants for handling different types of URIs and paths.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class UriConstants
{
    /// <summary>
    /// URI scheme for Avalonia embedded resources.
    /// </summary>
    public const string AvarUriScheme = "avares://";

    /// <summary>
    /// HTTP URI scheme.
    /// </summary>
    public const string HttpUriScheme = "http://";

    /// <summary>
    /// HTTPS URI scheme.
    /// </summary>
    public const string HttpsUriScheme = "https://";

    /// <summary>
    /// Base watch URL prefix for YouTube videos.
    /// </summary>
    public const string YouTubeWatchUrlPrefix = "https://www.youtube.com/watch?v=";

    /// <summary>
    /// Icon URI for Generals game type.
    /// </summary>
    public const string GeneralsIconUri = "avares://GenHub/Assets/Icons/generals-icon.png";

    /// <summary>
    /// Icon URI for Zero Hour game type.
    /// </summary>
    public const string ZeroHourIconUri = "avares://GenHub/Assets/Icons/zerohour-icon.png";

    /// <summary>
    /// Default icon URI for unknown game types.
    /// </summary>
    public const string DefaultIconUri = "avares://GenHub/Assets/Icons/generalshub-icon.png";

    /// <summary>
    /// Icon URI for GenPatcher tool.
    /// </summary>
    public const string GenPatcherIconUri = "avares://GenHub/Assets/Icons/genpatcher-icon.png";

    /// <summary>
    /// Icon URI for Replay Manager tool.
    /// </summary>
    public const string ReplayManagerIconUri = "avares://GenHub/Assets/Icons/replaymanager-icon.png";

    /// <summary>
    /// Icon URI for Map Manager tool.
    /// </summary>
    public const string MapManagerIconUri = "avares://GenHub/Assets/Icons/mapmanager-icon.png";

    /// <summary>
    /// Icon URI for Hotkeys Editor tool.
    /// </summary>
    public const string HotkeysEditorIconUri = "avares://GenHub/Assets/Icons/hotkeyseditor-icon.png";

    /// <summary>
    /// Icon URI for ModBuilder tool.
    /// </summary>
    public const string ModBuilderIconUri = "avares://GenHub/Assets/Icons/modbuilder-icon.png";

    // Icon Path Constants

    /// <summary>
    /// Base path for icon assets.
    /// </summary>
    public const string IconsBasePath = "/Assets/Icons";

    /// <summary>
    /// Filename for Generals icon.
    /// </summary>
    public const string GeneralsIconFilename = "generals-icon.png";

    /// <summary>
    /// Filename for Zero Hour icon.
    /// </summary>
    public const string ZeroHourIconFilename = "zerohour-icon.png";

    /// <summary>
    /// Substring marker for Zero Hour icon asset.
    /// </summary>
    public const string ZeroHourIconMarker = "zerohour-icon";

    /// <summary>
    /// Filename for GenHub default icon.
    /// </summary>
    public const string GenHubIconFilename = "generalshub-icon.png";

    /// <summary>
    /// Substring marker for GenHub default icon asset.
    /// </summary>
    public const string GenHubIconMarker = "generalshub-icon";

    /// <summary>
    /// Filename for Steam platform icon.
    /// </summary>
    public const string SteamIconFilename = "steam-icon.png";

    /// <summary>
    /// Filename for EA App platform icon.
    /// </summary>
    public const string EaAppIconFilename = "eaapp-icon.png";

    /// <summary>
    /// Filename for GenPatcher icon.
    /// </summary>
    public const string GenPatcherIconFilename = "genpatcher-icon.png";

    /// <summary>
    /// Filename for Replay Manager icon.
    /// </summary>
    public const string ReplayManagerIconFilename = "replaymanager-icon.png";

    /// <summary>
    /// Filename for Map Manager icon.
    /// </summary>
    public const string MapManagerIconFilename = "mapmanager-icon.png";

    /// <summary>
    /// Filename for Hotkeys Editor icon.
    /// </summary>
    public const string HotkeysEditorIconFilename = "hotkeyseditor-icon.png";

    /// <summary>
    /// Filename for ModBuilder icon.
    /// </summary>
    public const string ModBuilderIconFilename = "modbuilder-icon.png";

    // Cover Path Constants

    /// <summary>
    /// Base path for cover assets.
    /// </summary>
    public const string CoversBasePath = "/Assets/Covers";

    /// <summary>
    /// Directory path for cover assets with trailing slash.
    /// </summary>
    public const string CoversDirectoryPath = "/Assets/Covers/";

    /// <summary>
    /// Filename for Generals cover.
    /// </summary>
    public const string GeneralsCoverFilename = "generals-cover.png";

    /// <summary>
    /// Filename for Zero Hour cover.
    /// </summary>
    public const string ZeroHourCoverFilename = "zerohour-cover.png";

    /// <summary>
    /// Substring marker for Zero Hour cover asset.
    /// </summary>
    public const string ZeroHourCoverMarker = "zerohour-cover";

    /// <summary>
    /// Filename for China cover.
    /// </summary>
    public const string ChinaCoverFilename = "china-cover.jpg";

    /// <summary>
    /// Filename for USA cover.
    /// </summary>
    public const string UsaCoverFilename = "usa-cover.jpg";

    /// <summary>
    /// Filename for GLA cover.
    /// </summary>
    public const string GlaCoverFilename = "gla-cover.jpg";

    /// <summary>
    /// Legacy base path for image assets.
    /// </summary>
    public const string LegacyImagesBasePath = "/Assets/Images/";

    /// <summary>
    /// Legacy filename for China poster.
    /// </summary>
    public const string LegacyChinaPosterFilename = "china-poster.png";

    /// <summary>
    /// Legacy filename for USA poster.
    /// </summary>
    public const string LegacyUsaPosterFilename = "usa-poster.png";

    /// <summary>
    /// Legacy filename for GLA poster.
    /// </summary>
    public const string LegacyGlaPosterFilename = "gla-poster.png";

    /// <summary>
    /// Legacy filename for China cover before the JPEG re-encode.
    /// </summary>
    public const string LegacyChinaCoverPngFilename = "china-cover.png";

    /// <summary>
    /// Legacy filename for USA cover before the JPEG re-encode.
    /// </summary>
    public const string LegacyUsaCoverPngFilename = "usa-cover.png";

    /// <summary>
    /// Legacy filename for GLA cover before the JPEG re-encode.
    /// </summary>
    public const string LegacyGlaCoverPngFilename = "gla-cover.png";

    // Logo Path Constants

    /// <summary>
    /// Logo URI for Generals Online.
    /// </summary>
    public const string GeneralsOnlineLogoUri = "avares://GenHub/Assets/Logos/generalsonline-logo.png";

    /// <summary>
    /// Logo URI for The Super Hackers.
    /// </summary>
    public const string SuperHackersLogoUri = "avares://GenHub/Assets/Logos/thesuperhackers-logo.png";
}
