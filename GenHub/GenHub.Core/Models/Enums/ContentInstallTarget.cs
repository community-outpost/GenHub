namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the target installation location for content.
/// Different content types may need to be installed to different locations.
/// </summary>
public enum ContentInstallTarget
{
    /// <summary>
    /// Install to the game's workspace directory (default).
    /// Used for game clients, patches, mods, and most addons.
    /// </summary>
    Workspace = 0,

    /// <summary>
    /// Install to the user's Documents folder for the specific game.
    /// Location: Documents\Command and Conquer Generals Zero Hour Data\
    /// Used for maps, replays, and other user-specific content.
    /// Note: This path is used for Zero Hour content. Vanilla Generals content
    /// would use a different flow (not currently implemented in this enum).
    /// </summary>
    UserDataDirectory = 1,

    /// <summary>
    /// Install to the Maps subdirectory within user data.
    /// Location: Documents\Command and Conquer Generals Zero Hour Data\Maps\
    /// Used for custom maps.
    /// </summary>
    UserMapsDirectory = 2,

    /// <summary>
    /// Install to the Replays subdirectory within user data.
    /// Location: Documents\Command and Conquer Generals Zero Hour Data\Replays\
    /// Used for replay files.
    /// </summary>
    UserReplaysDirectory = 3,

    /// <summary>
    /// Install to the Screenshots subdirectory within user data.
    /// Location: Documents\Command and Conquer Generals Zero Hour Data\Screenshots\
    /// </summary>
    UserScreenshotsDirectory = 4,

    /// <summary>
    /// Install to system location (for prerequisites like VC++ redistributables).
    /// Requires elevation.
    /// </summary>
    System = 5,
}
