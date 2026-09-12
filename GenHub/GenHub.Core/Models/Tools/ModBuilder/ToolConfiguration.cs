using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents external tool configuration.
/// </summary>
public class ToolConfiguration
{
    /// <summary>
    /// Gets or sets the absolute path to the tool executable.
    /// </summary>
    [JsonPropertyName("absExe")]
    public string AbsExe { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA256 hash for tool verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
