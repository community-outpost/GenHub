namespace GenHub.Linux.Model;

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
    public int id { get; set; }

    public string slug { get; set; }

    public string runner { get; set; }

    public string platform { get; set; }

    public string year { get; set; }

    public string directory { get; set; }

    public string playtime { get; set; }

    public string lastplayed { get; set; }
}