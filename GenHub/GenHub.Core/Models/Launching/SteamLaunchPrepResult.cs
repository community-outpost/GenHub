namespace GenHub.Core.Models.Launching;

/// <summary>
/// Result of preparing a game directory for Steam-tracked profile launch.
/// </summary>
public record SteamLaunchPrepResult
{
    /// <summary>
    /// Gets the path to the executable to launch.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Gets the working directory for the launch.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the profile ID that was prepared.
    /// </summary>
    public required string ProfileId { get; init; }

    /// <summary>
    /// Gets the number of files that were linked into the game directory.
    /// </summary>
    public int FilesLinked { get; init; }

    /// <summary>
    /// Gets the number of files that were removed from the previous profile.
    /// </summary>
    public int FilesRemoved { get; init; }

    /// <summary>
    /// Gets the number of extraneous files that were backed up.
    /// </summary>
    public int FilesBackedUp { get; init; }

    /// <summary>
    /// Gets the Steam AppID if Steam launch is enabled.
    /// </summary>
    public string? SteamAppId { get; init; }
}