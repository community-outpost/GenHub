using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Enhanced file entry with advanced sourcing and handling options.
/// </summary>
public class ManifestFile
{
    /// <summary>
    /// Gets or sets the relative path of the file from the root directory.
    /// </summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the size of the file in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash of the file contents.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type for workspace preparation.
    /// </summary>
    public ManifestFileSourceType SourceType { get; set; }

    /// <summary>
    /// Gets or sets the file permissions for cross-platform compatibility.
    /// </summary>
    public FilePermissions Permissions { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the file is executable.
    /// </summary>
    public bool IsExecutable { get; set; }

    /// <summary>
    /// Gets or sets the download URL for remote files.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;
}
