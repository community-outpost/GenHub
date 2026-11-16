using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents folder paths for build outputs.
/// </summary>
public class FolderConfiguration
{
    /// <summary>
    /// Gets or sets the absolute path to the build directory.
    /// </summary>
    [JsonPropertyName("absBuildDir")]
    public string AbsBuildDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the release directory.
    /// </summary>
    [JsonPropertyName("absReleaseDir")]
    public string AbsReleaseDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the game installation directory.
    /// </summary>
    [JsonPropertyName("absGameDir")]
    public string AbsGameDir { get; set; } = string.Empty;
}
