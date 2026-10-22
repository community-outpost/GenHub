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
    /// Gets or sets the list of delimiter marker pairs to exclude from text files.
    /// </summary>
    [JsonPropertyName("excludeMarkersList")]
    public List<List<string>>? ExcludeMarkersList { get; set; }

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

    /// <summary>
    /// Creates a copy of this <see cref="BundleFile"/> instance.
    /// </summary>
    /// <returns>A cloned copy of this instance.</returns>
    public BundleFile Clone()
    {
        return new BundleFile
        {
            AbsSourceParent = AbsSourceParent,
            AbsSourceFile = AbsSourceFile,
            RelTargetFile = RelTargetFile,
            Params = Params != null ? new Dictionary<string, object>(Params) : null,
            ExcludeMarkersList = ExcludeMarkersList != null ? ExcludeMarkersList.Select(l => new List<string>(l)).ToList() : null,
            RegistryDef = RegistryDef != null ? new BundleRegistryDefinition(new List<string>(RegistryDef.Paths)) : null,
        };
    }
}
