using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using Velopack;

namespace GenHub.Features.AppUpdate.Interfaces;

/// <summary>
/// Interface for Velopack update manager operations.
/// </summary>
public interface IVelopackUpdateManager
{
    /// <summary>
    /// Checks for available updates from GitHub Releases.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>UpdateInfo if an update is available, otherwise null.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for available artifact updates from GitHub Actions CI builds.
    /// Requires a GitHub PAT with repo access.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ArtifactUpdateInfo if an artifact update is available, otherwise null.</returns>
    Task<ArtifactUpdateInfo?> CheckForArtifactUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of available branches from the repository.
    /// Requires a GitHub PAT with repo access.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of branch names.</returns>
    Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of open pull requests with available CI artifacts.
    /// Requires a GitHub PAT with repo access.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of open PRs with artifact info.</returns>
    Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of all available artifacts for a specific pull request.
    /// </summary>
    /// <param name="prNumber">The PR number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of artifacts for the PR.</returns>
    Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForPullRequestAsync(int prNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of all available artifacts for a specific branch.
    /// </summary>
    /// <param name="branchName">The branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of artifacts for the branch.</returns>
    Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForBranchAsync(string branchName, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Gets a value indicating whether artifact updates are available (requires PAT).
    /// </summary>
    bool HasArtifactUpdateAvailable { get; }

    /// <summary>
    /// Gets the latest artifact update info, if available.
    /// </summary>
    ArtifactUpdateInfo? LatestArtifactUpdate { get; }

    /// <summary>
    /// Gets or sets the subscribed PR number. Set to null for branch-based updates.
    /// </summary>
    int? SubscribedPrNumber { get; set; }

    /// <summary>
    /// Gets or sets the subscribed branch name. Set to null for release-based updates.
    /// </summary>
    string? SubscribedBranch { get; set; }

    /// <summary>
    /// Gets a value indicating whether the subscribed PR has been merged or closed.
    /// Used to trigger fallback to MAIN branch.
    /// </summary>
    bool IsPrMergedOrClosed { get; }

    /// <summary>
    /// Downloads and installs a specific artifact.
    /// </summary>
    /// <param name="artifactInfo">The artifact information to install.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the installation operation.</returns>
    Task InstallArtifactAsync(ArtifactUpdateInfo artifactInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and installs a PR artifact.
    /// </summary>
    /// <param name="prInfo">The PR information containing the artifact to install.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the installation operation.</returns>
    Task InstallPrArtifactAsync(PullRequestInfo prInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls the application.
    /// </summary>
    void Uninstall();

    /// <summary>
    /// Clears all cached update and artifact information.
    /// </summary>
    void ClearCache();
}
