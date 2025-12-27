using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.UserData;

/// <summary>
/// Represents a single file that has been installed to the user's data directory.
/// Tracks file metadata for cleanup and conflict resolution.
/// </summary>
public class UserDataFileEntry
{
    /// <summary>
    /// Gets or sets the relative path within the target directory.
    /// For UserMapsDirectory, this is relative to Documents\{GameDataFolder}\Maps\.
    /// Example: "OilDerrick/OilDerrick.map".
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full absolute path to the installed file.
    /// Example: "C:\Users\User\Documents\Command and Conquer Generals Zero Hour Data\Maps\OilDerrick\OilDerrick.map".
    /// </summary>
    public string AbsolutePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the file when it was installed.
    /// Used to detect if the user has modified the file.
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes when installed.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets the install target (Maps, Replays, etc.).
    /// </summary>
    public ContentInstallTarget InstallTarget { get; set; } = ContentInstallTarget.UserMapsDirectory;

    /// <summary>
    /// Gets or sets a value indicating whether this file overwrote an existing file.
    /// </summary>
    public bool WasOverwritten { get; set; }

    /// <summary>
    /// Gets or sets the backup path if an existing file was backed up.
    /// Null if no backup was needed.
    /// </summary>
    public string? BackupPath { get; set; }

    /// <summary>
    /// Gets or sets when this file was installed.
    /// </summary>
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether this entry is a hard link to CAS content.
    /// Hard links are preferred for efficient disk usage when on the same volume.
    /// </summary>
    public bool IsHardLink { get; set; }

    /// <summary>
    /// Gets or sets the CAS hash if this file is linked from CAS storage.
    /// Null if the file was copied directly.
    /// </summary>
    public string? CasHash { get; set; }
}
