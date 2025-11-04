using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Response model for Generals Online CDN API (manifest.json endpoint).
/// This model deserializes the JSON response from the CDN's manifest endpoint.
/// </summary>
public class GeneralsOnlineApiResponse
{
    /// <summary>
    /// Gets or sets the version of the release (e.g., "101525_QFE5").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download URL for the portable ZIP release.
    /// </summary>
    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the release notes/changelog.
    /// </summary>
    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash of the ZIP file for integrity verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}
