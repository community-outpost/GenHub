using System.Text.Json.Serialization;

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
    /// Gets or sets the SHA-256 hash of the uploaded file for deduplication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileHash { get; set; }

    /// <summary>
    /// Gets or sets the file key assigned by the cloud storage provider.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileKey { get; set; }

    /// <summary>
    /// Gets or sets the cryptographic HMAC deletion token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeleteToken { get; set; }

    /// <summary>
    /// Gets or sets the category or tool identifier of the upload (e.g. "replays", "maps").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a legacy record was pending deletion.
    /// </summary>
    /// <remarks>
    /// Retained only to migrate existing history files. New records leave this value unset.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPendingDeletion { get; set; }
}
