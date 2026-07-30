namespace GenHub.Core.Models.Launching;

/// <summary>
/// Cheap fingerprint of one retail archive root: a per-archive list of name, size and
/// timestamp from a single directory listing, never content hashes, so revalidation never
/// rereads gigabytes of archives.
/// </summary>
public class LaunchReceiptArchiveRoot
{
    /// <summary>Gets or sets the archive root path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of archives in the root; a summary of <see cref="Archives"/>.</summary>
    public int ArchiveCount { get; set; }

    /// <summary>Gets or sets the total size of the archives in the root, in bytes; a summary of <see cref="Archives"/>.</summary>
    public long TotalArchiveBytes { get; set; }

    /// <summary>Gets or sets the fingerprint of each archive in the root.</summary>
    public List<LaunchReceiptArchiveEntry> Archives { get; set; } = [];
}
