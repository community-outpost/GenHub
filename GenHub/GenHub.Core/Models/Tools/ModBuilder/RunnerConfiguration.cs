using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents game runner configuration.
/// </summary>
public class RunnerConfiguration
{
    /// <summary>
    /// Gets or sets the absolute path to the game executable.
    /// </summary>
    [JsonPropertyName("absExe")]
    public string AbsExe { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command-line arguments for the game.
    /// </summary>
    [JsonPropertyName("args")]
    public string Args { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the working directory for the game process.
    /// </summary>
    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the mod folder for native game -mod command line argument.
    /// </summary>
    [JsonPropertyName("modFolder")]
    public string ModFolder { get; set; } = string.Empty;
}
