namespace GenHub.Core.Models.Launching;

/// <summary>
/// Tracking data for Steam-tracked profile launches.
/// Stored in .genhub-files.json in the game installation directory.
/// </summary>
public class SteamLaunchTrackingData
{
    /// <summary>
    /// Gets or sets the ID of the profile that was last launched.
    /// </summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this profile was last launched.
    /// </summary>
    public DateTime LastLaunched { get; set; }

    /// <summary>
    /// Gets or sets the set of files managed by GenHub in this directory.
    /// These are files that were provisioned by GenHub and can be safely removed.
    /// </summary>
    public HashSet<string> ManagedFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of original files that were backed up.
    /// These files existed before GenHub provisioned files and should be restored when no longer needed.
    /// </summary>
    public List<BackedUpFile> BackedUpFiles { get; set; } = [];
}
