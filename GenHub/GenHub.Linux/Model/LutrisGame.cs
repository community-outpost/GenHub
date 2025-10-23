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
    /// The runner used to launch the game (e.g., "wine", "steam", "dosbox").
    /// </summary>
    public string runner { get; set; } = string.Empty;

    /// <summary>
    /// The runner used to launch the game (e.g., "wine", "Linux").
    /// </summary>
    public string platform { get; set; } = string.Empty;

    /// <summary>
    /// An associated year value; the specific meaning or accuracy may be inconsistent.
    /// </summary>
    public string year { get; set; } = string.Empty;

    /// <summary>
    /// The local installation directory path of the launcher (e.g., EA App).
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
