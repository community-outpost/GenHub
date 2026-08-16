using System.IO.Compression;
using System.Text.Json.Serialization;
using GenHub.Core.Models.Tools.ModBuilder.Converters;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the complete build configuration loaded from JSON files.
/// </summary>
public class BuildConfiguration
{
    /// <summary>
    /// Gets or sets the list of bundle items to build.
    /// </summary>
    [JsonPropertyName("items")]
    public List<BundleItem> Items { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of bundle packs for distribution.
    /// </summary>
    [JsonPropertyName("packs")]
    [JsonConverter(typeof(BundlePackListConverter))]
    public List<BundlePack> Packs { get; set; } = new();

    /// <summary>
    /// Gets or sets the folder configuration for build outputs.
    /// </summary>
    [JsonPropertyName("folders")]
    public FolderConfiguration Folders { get; set; } = new();

    /// <summary>
    /// Gets or sets the game runner configuration.
    /// </summary>
    [JsonPropertyName("runner")]
    public RunnerConfiguration Runner { get; set; } = new();

    /// <summary>
    /// Gets or sets the external tools configuration.
    /// </summary>
    [JsonPropertyName("tools")]
    public Dictionary<string, ToolConfiguration> Tools { get; set; } = new();

    /// <summary>
    /// Gets or sets the compression level for ZIP archives.
    /// </summary>
    /// <remarks>
    /// Defaults to Fastest for better dev build performance.
    /// Use Optimal for release builds to minimize file size.
    /// Use NoCompression for debugging archive issues.
    /// </remarks>
    [JsonPropertyName("compressionLevel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompressionLevel ZipCompressionLevel { get; set; } = CompressionLevel.Fastest;

    /// <summary>
    /// Gets or sets the configuration file paths that were loaded.
    /// </summary>
    [JsonIgnore]
    public List<string> LoadedConfigFiles { get; set; } = new();
}
