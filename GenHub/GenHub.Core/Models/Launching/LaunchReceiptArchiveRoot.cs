namespace GenHub.Core.Models.Launching;

/// <summary>
/// Cheap fingerprint of one retail archive root: archive count and total bytes rather than
/// content hashes, so revalidation never rereads gigabytes of archives.
/// </summary>
public class LaunchReceiptArchiveRoot
{
    /// <summary>Gets or sets the archive root path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of archives in the root.</summary>
    public int ArchiveCount { get; set; }

    /// <summary>Gets or sets the total size of the archives in the root, in bytes.</summary>
    public long TotalArchiveBytes { get; set; }
}
