using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a source-to-target file mapping with conversion parameters for the build system.
/// </summary>
public class BundleFile
{
    /// <summary>
    /// Gets or sets the absolute path to the source file's parent directory.
    /// </summary>
    [JsonPropertyName("absSourceParent")]
    public string AbsSourceParent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the source file.
    /// </summary>
    [JsonPropertyName("absSourceFile")]
    public string AbsSourceFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative path for the target file.
    /// </summary>
    [JsonPropertyName("relTargetFile")]
    public string RelTargetFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the conversion parameters for this file.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }

    /// <summary>
    /// Gets or sets the file hash registry definition for change detection.
    /// </summary>
    [JsonPropertyName("registry")]
    public BundleRegistryDefinition? RegistryDef { get; set; }

    /// <summary>
    /// Gets the relative source file path by removing the parent directory prefix.
    /// </summary>
    /// <returns>The relative source file path.</returns>
    public string GetRelSourceFile()
    {
        if (string.IsNullOrEmpty(AbsSourceParent) || string.IsNullOrEmpty(AbsSourceFile))
            return string.Empty;

        var normalized = Path.GetFullPath(AbsSourceFile);
        var parent = Path.GetFullPath(AbsSourceParent);

        if (normalized.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(parent.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return AbsSourceFile;
    }
}
