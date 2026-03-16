using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a downloadable file artifact within a release.
/// </summary>
public class ReleaseArtifact
{
    /// <summary>
    /// Gets or sets the artifact filename (e.g., "MyMod-1.0.0.zip").
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct download URL.
    /// Supports GitHub Releases, ModDB, generic HTTP, Google Drive, Dropbox, etc.
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash for integrity verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the artifact.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the primary artifact.
    /// When multiple artifacts exist, the primary one is downloaded by default.
    /// </summary>
    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; } = true;
}
