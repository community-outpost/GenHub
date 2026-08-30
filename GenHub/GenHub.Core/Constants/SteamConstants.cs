namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to Steam integration.
/// </summary>
public static class SteamConstants
{
    /// <summary>
    /// Steam AppID for Command &amp; Conquer: Generals.
    /// </summary>
    public const string GeneralsAppId = "17300";

    /// <summary>
    /// Steam AppID for Command &amp; Conquer: Generals - Zero Hour.
    /// </summary>
    public const string ZeroHourAppId = "2732960";

    /// <summary>
    /// The name of the tracking file used for Steam launches.
    /// </summary>
    public const string TrackingFileName = ".genhub-files.json";

    /// <summary>
    /// The name of the backup directory for original game files.
    /// </summary>
    public const string BackupDirName = ".genhub-backup";

    /// <summary>
    /// The extension used for backed up game executables.
    /// </summary>
    public const string BackupExtension = FileTypes.BackupExtension;

    /// <summary>
    /// The filename of the proxy launcher executable.
    /// </summary>
    public const string ProxyLauncherFileName = "GenHub.ProxyLauncher.exe";
}
