namespace GenHub.Linux.Model;

/// <summary>
/// Data structure for parsing Lutris game list output.
/// </summary>
public class LutrisGame
{
    /// <summary>
    /// The unique identifier for the game within Lutris.
    /// </summary>
    public int id { get; set; } = 0;

    /// <summary>
    /// slug of the game (similar to name).
    /// </summary>
    public string slug { get; set; } = string.Empty;

    /// <summary>
    /// name of the game.
    /// </summary>
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// The runner used to launch the game (e.g., "wine", "steam", "dosbox").
    /// </summary>
    public string runner { get; set; } = string.Empty;

    /// <summary>
    /// The runner used to launch the game (e.g., "wine", "Linux").
    /// </summary>
    public string platform { get; set; } = string.Empty;

   /// <summary>
    /// The official release year of the game, as sourced from Lutris metadata.
    /// </summary>
    public int year { get; set; } = 0;

    /// <summary>
    /// The local installation directory path of the game.
    /// in case of zero hour it will be EA App launcher.
    /// </summary>
    public string directory { get; set; } = string.Empty;

    /// <summary>
    /// The total time the user has spent playing the game.
    /// </summary>
    public string playtime { get; set; } = string.Empty;

    /// <summary>
    /// The timestamp indicating when the game was last played.
    /// </summary>
    public string lastplayed { get; set; } = string.Empty;
}
