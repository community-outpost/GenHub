using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.AppUpdate;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;
using Velopack;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Update manager for platforms that publish no update artifacts.
/// <para>
/// Registering this in a platform host disables self-update entirely for that host.
/// Every query reports "nothing available" and every mutation is a logged no-op, so
/// the update UI stays quiet rather than offering an update that cannot be applied.
/// </para>
/// <para>
/// This exists because <see cref="VelopackUpdateManager"/> selects release and CI
/// artifacts by matching a platform substring against the artifact name. A platform
/// with no published artifacts has no safe behaviour there: the best case is wasted
/// GitHub API calls on every check, and the worst is applying a package built for a
/// different operating system, which leaves an install that cannot start and cannot
/// be rolled back.
/// </para>
/// <para>
/// Remove the host's registration once that platform publishes artifacts; the real
/// manager then takes over with no other change.
/// </para>
/// </summary>
/// <param name="logger">Logger used to record suppressed update operations.</param>
public sealed class UnsupportedPlatformUpdateManager(
    ILogger<UnsupportedPlatformUpdateManager> logger) : IVelopackUpdateManager
{
    private const string Reason = "Self-update is not supported on this platform (no update artifacts are published for it).";

    /// <inheritdoc/>
    public bool IsUpdatePendingRestart => false;

    /// <inheritdoc/>
    public bool HasUpdateAvailableFromGitHub => false;

    /// <inheritdoc/>
    public string? LatestVersionFromGitHub => null;

    /// <inheritdoc/>
    public bool HasArtifactUpdateAvailable => false;

    /// <inheritdoc/>
    public ArtifactUpdateInfo? LatestArtifactUpdate => null;

    /// <inheritdoc/>
    public bool IsPrMergedOrClosed => false;

    /// <summary>
    /// Gets or sets the subscribed PR number. Accepted and retained so settings round-trip,
    /// but never acted on.
    /// </summary>
    public int? SubscribedPrNumber { get; set; }

    /// <summary>
    /// Gets or sets the subscribed branch. Accepted and retained so settings round-trip,
    /// but never acted on.
    /// </summary>
    public string? SubscribedBranch { get; set; }

    /// <inheritdoc/>
    public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(CheckForUpdatesAsync));
        return Task.FromResult<UpdateInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<ArtifactUpdateInfo?> CheckForArtifactUpdatesAsync(CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(CheckForArtifactUpdatesAsync));
        return Task.FromResult<ArtifactUpdateInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(GetBranchesAsync));
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(GetOpenPullRequestsAsync));
        return Task.FromResult<IReadOnlyList<PullRequestInfo>>([]);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForPullRequestAsync(int prNumber, CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(GetArtifactsForPullRequestAsync));
        return Task.FromResult<IReadOnlyList<ArtifactUpdateInfo>>([]);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(GetArtifactsForBranchAsync));
        return Task.FromResult<IReadOnlyList<ArtifactUpdateInfo>>([]);
    }

    /// <inheritdoc/>
    public Task DownloadUpdatesAsync(UpdateInfo updateInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(DownloadUpdatesAsync));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task InstallArtifactAsync(ArtifactUpdateInfo artifactInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(InstallArtifactAsync));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task InstallPrArtifactAsync(PullRequestInfo prInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        LogSuppressed(nameof(InstallPrArtifactAsync));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo) => LogSuppressed(nameof(ApplyUpdatesAndRestart));

    /// <inheritdoc/>
    public void ApplyUpdatesAndExit(UpdateInfo updateInfo) => LogSuppressed(nameof(ApplyUpdatesAndExit));

    /// <inheritdoc/>
    public void Uninstall() => LogSuppressed(nameof(Uninstall));

    /// <inheritdoc/>
    public void ClearCache()
    {
        // Nothing is ever cached, so this is genuinely a no-op rather than a suppression.
    }

    private void LogSuppressed(string operation) =>
        logger.LogDebug("{Operation} suppressed. {Reason}", operation, Reason);
}
