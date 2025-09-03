namespace GenHub.Core.Models.Storage;

/// <summary>
/// Statistics about the Content-Addressable Storage system.
/// </summary>
public class CasStats
{
    /// <summary>
    /// Gets or sets the total number of objects in the CAS.
    /// In a Content-Addressable Storage system, this equals the number of unique objects
    /// since duplicates are automatically deduplicated by content hash.
    /// </summary>
    public int ObjectCount { get; set; }

    /// <summary>
    /// Gets or sets the total size of all objects in bytes.
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Gets or sets the total space saved through deduplication in bytes.
    /// </summary>
    public long SpaceSaved { get; set; }

    /// <summary>
    /// Gets or sets the hit rate for CAS lookups (0.0 to 1.0).
    /// </summary>
    [System.ComponentModel.DataAnnotations.Range(0.0, 1.0)]
    public double HitRate { get; set; }

    /// <summary>
    /// Gets or sets the number of objects accessed in the last period.
    /// </summary>
    public int RecentAccesses { get; set; }
}
