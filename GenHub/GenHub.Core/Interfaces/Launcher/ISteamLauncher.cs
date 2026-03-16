using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Launcher;

/// <summary>
/// Service for preparing game directories for Steam-tracked profile launches.
/// This approach provisions mod files directly to the game installation directory,
/// enabling native Steam integration (overlay and playtime tracking).
/// </summary>
public interface ISteamLauncher
{
    /// <summary>
    /// Prepares a game directory for Steam-tracked profile launch.
    /// </summary>
    /// <param name="gameInstallPath">The game installation directory path.</param>
    /// <param name="profileId">The profile ID being launched.</param>
    /// <param name="manifests">The content manifests for this profile.</param>
    /// <param name="executableName">The executable name to launch (e.g., "GeneralsOnlineZH_60.exe").</param>
    /// <param name="targetExecutablePath">The target executable path.</param>
    /// <param name="targetWorkingDirectory">The target working directory.</param>
    /// <param name="targetArguments">The target arguments.</param>
    /// <param name="steamAppId">Optional Steam AppID for tracking/overlay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing executable path and statistics.</returns>
    Task<OperationResult<SteamLaunchPrepResult>> PrepareForProfileAsync(
        string gameInstallPath,
        string profileId,
        IEnumerable<ContentManifest> manifests,
        string executableName,
        string targetExecutablePath,
        string targetWorkingDirectory,
        string[]? targetArguments = null,
        string? steamAppId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up all GenHub-managed files from a game directory and restores original executable.
    /// </summary>
    /// <param name="gameInstallPath">The game installation directory path.</param>
    /// <param name="executableName">The executable name that was replaced (e.g., "generals.exe").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> CleanupGameDirectoryAsync(
        string gameInstallPath,
        string executableName,
        CancellationToken cancellationToken = default);
}