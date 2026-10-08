using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents an event callback configuration for the build system.
/// </summary>
public class BundleEvent
{
    /// <summary>
    /// Gets or sets the type of event this callback handles.
    /// </summary>
    [JsonPropertyName("type")]
    public BundleEventType Type { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the script file containing the callback.
    /// </summary>
    [JsonPropertyName("absScript")]
    public string AbsScript { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the function to call in the script.
    /// </summary>
    [JsonPropertyName("funcName")]
    public string FuncName { get; set; } = "OnEvent";

    /// <summary>
    /// Gets or sets additional keyword arguments to pass to the callback function.
    /// </summary>
    [JsonPropertyName("kwargs")]
    public Dictionary<string, object> Kwargs { get; set; } = new();

    /// <summary>
    /// Gets the directory containing the script file.
    /// </summary>
    /// <returns>The directory path containing the script file.</returns>
    public string GetScriptDir()
    {
        return Path.GetDirectoryName(AbsScript) ?? string.Empty;
    }

    /// <summary>
    /// Gets the script file name without extension.
    /// </summary>
    /// <returns>The script file name without extension.</returns>
    public string GetScriptName()
    {
        return Path.GetFileNameWithoutExtension(AbsScript);
    }
}
