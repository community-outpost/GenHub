using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.AppUpdate;
using Velopack;

namespace GenHub.Features.AppUpdate.Interfaces;

/// <summary>
/// Interface for Velopack update manager operations.
/// </summary>
public interface IVelopackUpdateManager
{
    /// <summary>
    /// Checks for available updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>UpdateInfo if an update is available, otherwise null.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the specified update.
    /// </summary>
    /// <param name="updateInfo">The update information.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    Task DownloadUpdatesAsync(UpdateInfo updateInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// </summary>
    /// <param name="updateInfo">The update information.</param>
    void ApplyUpdatesAndRestart(UpdateInfo updateInfo);

    /// <summary>
    /// Applies the downloaded update and exits the application.
    /// </summary>
    /// <param name="updateInfo">The update information.</param>
    void ApplyUpdatesAndExit(UpdateInfo updateInfo);

    /// <summary>
    /// Gets a value indicating whether an update is pending restart.
    /// </summary>
    bool IsUpdatePendingRestart { get; }

    /// <summary>
    /// Gets a value indicating whether an update is available from GitHub API check.
    /// This is true even when running from debug (where UpdateManager can't install).
    /// </summary>
    bool HasUpdateAvailableFromGitHub { get; }

    /// <summary>
    /// Gets the latest version available from GitHub, if an update was found.
    /// </summary>
    string? LatestVersionFromGitHub { get; }
}
