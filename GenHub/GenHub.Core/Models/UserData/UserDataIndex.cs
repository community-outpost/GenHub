namespace GenHub.Core.Models.UserData;

/// <summary>
/// Index of all user data installations across all profiles and manifests.
/// Provides quick lookup capabilities for the user data tracking system.
/// </summary>
public class UserDataIndex
{
    /// <summary>
    /// Gets or sets the version of the index format.
    /// Used for migration purposes.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets when the index was last updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the list of all installation keys (manifestId_profileId).
    /// </summary>
    public List<string> InstallationKeys { get; set; } = new();

    /// <summary>
    /// Gets or sets a dictionary mapping absolute file paths to their installation key.
    /// Enables quick conflict detection when installing new content.
    /// </summary>
    public Dictionary<string, string> FileToInstallationMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a dictionary mapping profile IDs to their installation keys.
    /// Enables quick lookup of all content installed for a profile.
    /// </summary>
    public Dictionary<string, List<string>> ProfileInstallations { get; set; } = new();

    /// <summary>
    /// Gets or sets a dictionary mapping manifest IDs to their installation keys.
    /// Enables quick lookup of all profiles using a manifest.
    /// </summary>
    public Dictionary<string, List<string>> ManifestInstallations { get; set; } = new();
}
