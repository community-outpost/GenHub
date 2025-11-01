using System.Text.Json.Serialization;

namespace GenHub.Linux.Model;

/// <summary>
/// Data structure for parsing Lutris game list output.
/// </summary>
public class LutrisGame
{
    /// <summary>
    /// Gets the unique identifier for the game within Lutris.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    /// <summary>
    /// Gets slug of the game (similar to name).
    /// </summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets name of the game.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the runner used to launch the game (e.g., "wine", "steam", "dosbox").
    /// </summary>
    [JsonPropertyName("runner")]
    public string Runner { get; set; } = string.Empty;

    /// <summary>
    /// Gets the platform used to launch the game (e.g., "wine", "Linux").
    /// </summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

   /// <summary>
    /// Gets the official release year of the game, as sourced from Lutris metadata.
    /// </summary>
    [JsonPropertyName("year")]
    public int Year { get; set; } = 0;

    /// <summary>
    /// Gets the local installation directory path of the game.
    /// in case of zero hour it will be EA App launcher.
    /// </summary>
    [JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Gets the total time the user has spent playing the game.
    /// </summary>
    [JsonPropertyName("playtime")]
    public string Playtime { get; set; } = string.Empty;

    /// <summary>
    /// Gets the timestamp indicating when the game was last played.
    /// </summary>
    [JsonPropertyName("lastplayed")]
    public string Lastplayed { get; set; } = string.Empty;
}
