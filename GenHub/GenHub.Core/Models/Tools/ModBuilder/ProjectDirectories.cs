using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the directory structure for a ModBuilder project.
/// </summary>
public class ProjectDirectories
{
    private string _configs = "Configs";
    private string _release = ".Release";

    /// <summary>
    /// Gets or sets the relative path to the configs directory.
    /// </summary>
    [JsonPropertyName("configs")]
    public string Configs
    {
        get => _configs;
        set => _configs = value;
    }

    /// <summary>
    /// Gets or sets the relative path to the configs directory (alias for compatibility).
    /// </summary>
    [JsonPropertyName("config")]
    public string Config
    {
        get => _configs;
        set => _configs = value;
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
    public string Release
    {
        get => _release;
        set => _release = value;
    }

    /// <summary>
    /// Gets or sets the relative path to the release directory (alias for compatibility).
    /// </summary>
    [JsonPropertyName("output")]
    public string Output
    {
        get => _release;
        set => _release = value;
    }
}
