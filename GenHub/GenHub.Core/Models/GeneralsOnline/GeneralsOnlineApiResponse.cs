using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents the API response from Generals Online CDN manifest endpoint.
/// </summary>
public class GeneralsOnlineApiResponse
{
    /// <summary>
    /// Gets or sets the version string (e.g., "111825_QFE2" or "042826_QFE3_EAC").
    /// Format: MMDDYY_QFE#[_SUFFIX] where MM=month, DD=day, YY=year, #=QFE number.
    /// The optional suffix (e.g. "_EAC") is build metadata that does not affect version ordering.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct download URL for the portable release.
    /// </summary>
    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the release notes or changelog.
    /// </summary>
    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash for file verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}
