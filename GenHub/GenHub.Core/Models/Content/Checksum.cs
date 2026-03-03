using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents checksum information for file integrity verification.
/// </summary>
public class Checksum
{
    /// <summary>
    /// Gets or sets the MD5 hash of the file.
    /// </summary>
    [JsonPropertyName("md5")]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the file.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
