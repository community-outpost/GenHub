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

    /// <summary>
    /// Creates a deep copy of the current <see cref="Checksum"/> instance.
    /// </summary>
    /// <returns>A new <see cref="Checksum"/> instance with identical values.</returns>
    public Checksum Clone() => new()
    {
        Md5 = Md5,
        Sha256 = Sha256,
    };
}
