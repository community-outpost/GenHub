using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.UserData;

/// <summary>
/// Tracks all files installed to user data directories for a specific content manifest.
/// Used for cleanup, conflict resolution, and profile-based content management.
/// </summary>
public class UserDataManifest
{
    /// <summary>
    /// Gets or sets the content manifest ID that owns these files.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game profile ID that installed this content.
    /// Content is tracked per-profile to support different map configurations.
    /// </summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target game type (Generals or Zero Hour).
    /// Determines which Documents folder the content was installed to.
    /// </summary>
    public GameType TargetGame { get; set; }

    /// <summary>
    /// Gets or sets the list of files installed by this manifest.
    /// </summary>
    public List<UserDataFileEntry> InstalledFiles { get; set; } = new();

    /// <summary>
    /// Gets or sets when this content was installed.
    /// </summary>
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the installation was last verified.
    /// </summary>
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this installation is currently active.
    /// Active installations have their content linked/present in the user data directory.
    /// Inactive installations have their content unlinked but tracked for potential reactivation.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the manifest version when installed.
    /// Used to detect if the content needs updating.
    /// </summary>
    public string ManifestVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total size of all installed files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets a unique key for this installation combining manifest and profile IDs.
    /// </summary>
    public string InstallationKey => $"{ManifestId}_{ProfileId}";
}
