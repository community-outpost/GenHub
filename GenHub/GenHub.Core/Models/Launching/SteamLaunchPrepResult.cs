namespace GenHub.Core.Models.Launching;

/// <summary>
/// Result of preparing a game directory for Steam-tracked profile launch.
/// </summary>
public class SteamLaunchPrepResult
{
    /// <summary>
    /// Gets or sets the path to the executable to launch.
    /// </summary>
    public required string ExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the launch.
    /// </summary>
    public required string WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the profile ID that was prepared.
    /// </summary>
    public required string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were linked into the game directory.
    /// </summary>
    public int FilesLinked { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were removed from the previous profile.
    /// </summary>
    public int FilesRemoved { get; set; }

    /// <summary>
    /// Gets or sets the number of extraneous files that were backed up.
    /// </summary>
    public int FilesBackedUp { get; set; }

    /// <summary>
    /// Gets or sets the Steam AppID if Steam launch is enabled.
    /// </summary>
    public string? SteamAppId { get; set; }
}