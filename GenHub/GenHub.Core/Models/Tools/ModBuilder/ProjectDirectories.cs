using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the directory structure for a ModBuilder project.
/// </summary>
public class ProjectDirectories
{
    /// <summary>
    /// Gets or sets the relative path to the configs directory.
    /// </summary>
    [JsonPropertyName("configs")]
    public string Configs { get; set; } = "Configs";

    /// <summary>
    /// Gets or sets the relative path to the configs directory (alias for compatibility).
    /// </summary>
    [JsonPropertyName("config")]
    public string Config
    {
        get => Configs;
        set => Configs = value;
    }

    /// <summary>
    /// Gets or sets the relative path to the game files edited directory.
    /// </summary>
    [JsonPropertyName("gameFilesEdited")]
    public string GameFilesEdited { get; set; } = "GameFilesEdited";

    /// <summary>
    /// Gets or sets the relative path to the build directory.
    /// </summary>
    [JsonPropertyName("build")]
    public string Build { get; set; } = ".Build";

    /// <summary>
    /// Gets or sets the relative path to the release directory.
    /// </summary>
    [JsonPropertyName("release")]
    public string Release { get; set; } = ".Release";

    /// <summary>
    /// Gets or sets the relative path to the release directory (alias for compatibility).
    /// </summary>
    [JsonPropertyName("output")]
    public string Output
    {
        get => Release;
        set => Release = value;
    }
}
