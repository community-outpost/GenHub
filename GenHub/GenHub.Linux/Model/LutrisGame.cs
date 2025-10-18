namespace GenHub.Linux.Model;

/// <summary>
/// data structure for parsing Lutris output.
/// </summary>
public class LutrisGame
{
    public int id { get; set; } = 0;

    public string slug { get; set; } = string.Empty;

    public string runner { get; set; } = string.Empty;

    public string platform { get; set; } = string.Empty;

    public string year { get; set; } = string.Empty;

    public string directory { get; set; } = string.Empty;

    public string playtime { get; set; } = string.Empty;

    public string lastplayed { get; set; } = string.Empty;
}