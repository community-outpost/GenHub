namespace GenHub.Core.Models.Launching;

/// <summary>
/// Cheap fingerprint of one archive within a retail root: name, size and timestamp, never
/// content. An equal-size replacement is visible through the timestamp where a count and
/// byte total alone could not see it.
/// </summary>
public class LaunchReceiptArchiveEntry
{
    /// <summary>Gets or sets the archive file name, without its directory.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the archive size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the archive's last write time, in UTC.</summary>
    public DateTime LastWriteUtc { get; set; }
}
