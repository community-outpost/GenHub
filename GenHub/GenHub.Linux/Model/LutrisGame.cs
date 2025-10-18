namespace GenHub.Linux.Model;

/// <summary>
/// data structure for parsing Lutris output.
/// </summary>
public class LutrisGame
{
    /*
     lutris output example
       "id": 1,
       "slug": "ea-app",
       "name": "EA App",
       "runner": "wine",
       "platform": "Windows",
       "year": 2022,
       "directory": "/home/kian/Games/ea-app",
       "playtime": "0:00:02.004862",
       "lastplayed": "2025-10-02 21:16:00"
     */
    public int id { get; set; } = 0;

    public string slug { get; set; } = string.Empty;

    public string runner { get; set; } = string.Empty;

    public string platform { get; set; } = string.Empty;

    public string year { get; set; } = string.Empty;

    public string directory { get; set; } = string.Empty;

    public string playtime { get; set; } = string.Empty;

    public string lastplayed { get; set; } = string.Empty;
}