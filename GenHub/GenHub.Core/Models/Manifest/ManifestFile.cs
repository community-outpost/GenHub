using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a file entry in a game manifest with content source information.
/// </summary>
public class ManifestFile
{
    /// <summary>
    /// Gets or sets the relative path of the file from the root directory.
    /// </summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type of this file.
    /// </summary>
    [JsonPropertyName("sourceType")]
    public ContentSourceType SourceType { get; set; } = ContentSourceType.Unknown;

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
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this file is required for the manifest to function.
    /// </summary>
    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the source path for copy operations, relative to a base installation or extraction path.
    /// </summary>
    [JsonPropertyName("sourcePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the path to the patch file to be applied to the target file.
    /// This is only used when SourceType is 'Patch'. The path is relative to the mod's own content root.
    /// </summary>
    [JsonPropertyName("patchSourceFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PatchSourceFile { get; set; }

    /// <summary>
    /// Gets or sets information for package extraction. This is only used when SourceType is 'Package'.
    /// </summary>
    [JsonPropertyName("packageInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtractionConfiguration? PackageInfo { get; set; }
}
