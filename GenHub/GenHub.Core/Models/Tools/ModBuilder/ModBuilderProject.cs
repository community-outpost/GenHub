using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a ModBuilder project container with metadata and configuration.
/// </summary>
public class ModBuilderProject
{
    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the project version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the project description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project author.
    /// </summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the project directory.
    /// </summary>
    [JsonPropertyName("projectDir")]
    public string ProjectDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the game installation.
    /// </summary>
    [JsonPropertyName("gameDir")]
    public string GameDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game installation ID (for linking to game profiles).
    /// </summary>
    [JsonPropertyName("gameInstallationId")]
    public string? GameInstallationId { get; set; }

    /// <summary>
    /// Gets or sets the project directory structure configuration.
    /// </summary>
    [JsonPropertyName("directories")]
    public ProjectDirectories Directories { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of configuration file paths to load.
    /// </summary>
    [JsonPropertyName("configFiles")]
    public List<string> ConfigFiles { get; set; } = new();

    /// <summary>
    /// Gets or sets the bundle configuration file paths.
    /// </summary>
    [JsonPropertyName("bundleConfigs")]
    public List<string> BundleConfigs { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of bundle packs in this project.
    /// </summary>
    [JsonPropertyName("bundlePacks")]
    public List<BundlePack> BundlePacks { get; set; } = new();

    /// <summary>
    /// Gets or sets the build configuration.
    /// </summary>
    [JsonIgnore]
    public BuildConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets or sets the date the project was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date the project was last modified.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date the project was last modified (alias for compatibility).
    /// </summary>
    [JsonPropertyName("lastModified")]
    public DateTime LastModified
    {
        get => ModifiedAt;
        set => ModifiedAt = value;
    }

    /// <summary>
    /// Gets or sets the date of the last successful build.
    /// </summary>
    [JsonPropertyName("lastBuild")]
    public DateTime? LastBuild { get; set; }

    /// <summary>
    /// Gets or sets additional project metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}
