using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents checksum information for file integrity verification.
/// </summary>
public class Checksum
{
    private string _md5 = string.Empty;
    private string _sha256 = string.Empty;

    /// <summary>
    /// Gets or sets the MD5 hash of the file.
    /// </summary>
    [JsonPropertyName("md5")]
    public string Md5
    {
        get => _md5;
        set => _md5 = value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the SHA-256 hash of the file.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256
    {
        get => _sha256;
        set => _sha256 = value?.Trim() ?? string.Empty;
    }

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
