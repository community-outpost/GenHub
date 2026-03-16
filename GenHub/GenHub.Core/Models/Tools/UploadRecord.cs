namespace GenHub.Core.Models.Tools;

/// <summary>
/// Record of an upload for rate limiting purposes.
/// </summary>
public sealed class UploadRecord
{
    /// <summary>
    /// Gets or sets the timestamp of the upload.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the size of the upload in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the public URL of the upload.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the name of the uploaded file.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is queued for deletion.
    /// </summary>
    public bool IsPendingDeletion { get; set; }
}