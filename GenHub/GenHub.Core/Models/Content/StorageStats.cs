namespace GenHub.Core.Models.Content;

/// <summary>
/// Storage statistics for the content storage service.
/// This DTO contains aggregate metrics about files stored under the content storage root.
/// </summary>
public class StorageStats
{
    /// <summary>
    /// Gets or sets the total size of stored content in bytes (sum of all file lengths under the storage root).
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of stored manifests (logical manifest files tracked by the service).
    /// Note: This counts manifest files (for example *.manifest.json) and is typically less than or equal
    /// to <see cref="TotalFileCount"/> which counts all files under the storage root.
    /// </summary>
    public int ManifestCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of files present in the storage root. This includes manifests,
    /// content files, deduplicated chunks/blobs, indexes, metadata and temporary files.
    /// </summary>
    public long TotalFileCount { get; set; }

    /// <summary>
    /// Gets or sets the amount of space saved through deduplication in bytes.
    /// If the storage implementation does not support deduplication this will be zero.
    /// </summary>
    public long DeduplicationSavingsBytes { get; set; }

    /// <summary>
    /// Gets or sets the available free space on the drive containing the storage root in bytes.
    /// </summary>
    public long AvailableFreeSpaceBytes { get; set; }
}