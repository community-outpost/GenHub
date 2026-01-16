using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using Velopack;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock implementation of IVelopackUpdateManager for interactive demos.
/// </summary>
public class MockVelopackUpdateManager(INotificationService? notificationService = null) : IVelopackUpdateManager
{
    private readonly INotificationService? _notificationService = notificationService;

    /// <inheritdoc/>
    public bool HasUpdateAvailableFromGitHub => true;

    /// <inheritdoc/>
    public string? LatestVersionFromGitHub => "0.0.5";

    /// <inheritdoc/>
    public bool IsUpdatePendingRestart => false;

    /// <inheritdoc/>
    public bool HasArtifactUpdateAvailable => false;

    /// <inheritdoc/>
    public ArtifactUpdateInfo? LatestArtifactUpdate => null;

    /// <inheritdoc/>
    public int? SubscribedPrNumber { get; set; }

    /// <inheritdoc/>
    public string? SubscribedBranch { get; set; }

    /// <inheritdoc/>
    public bool IsPrMergedOrClosed => false;

    /// <inheritdoc/>
    public void ApplyUpdatesAndExit(UpdateInfo updateInfo)
    {
    }

    /// <inheritdoc/>
    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo)
    {
        _notificationService?.Show(new NotificationMessage(
            NotificationType.Success,
            "Demo Update",
            "In a real installation, the app would restart now to apply the update!",
            5000));
    }

    /// <inheritdoc/>
    public Task<ArtifactUpdateInfo?> CheckForArtifactUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ArtifactUpdateInfo?>(null);

    /// <inheritdoc/>
    public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // Return null to simulate "check completed but no Velopack update found"
        // We will manually set state in the ViewModel
        return Task.FromResult<UpdateInfo?>(null);
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
    }

    /// <inheritdoc/>
    public Task DownloadUpdatesAsync(UpdateInfo updateInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Simulate download
        _ = Task.Run(
            async () =>
            {
                for (int i = 0; i <= 100; i += 10)
                {
                    progress?.Report(new UpdateProgress { PercentComplete = i, Status = "Downloading demo update..." });
                    await Task.Delay(200, cancellationToken);
                }
            },
            cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ArtifactUpdateInfo>
        {
            new("1.2.0", "abcdefg", null, 123456, "https://github.com", 7890, $"GenHub-win-x64-{branchName}", DateTime.Now.AddDays(-1), "https://github.com/download", 50 * 1024 * 1024),
            new("1.1.9", "7654321", null, 123455, "https://github.com", 7889, $"GenHub-win-x64-{branchName}", DateTime.Now.AddDays(-3), "https://github.com/download", 50 * 1024 * 1024),
        };
        return Task.FromResult<IReadOnlyList<ArtifactUpdateInfo>>(artifacts);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForPullRequestAsync(int prNumber, CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ArtifactUpdateInfo>
        {
            new("1.2.0", "abc1234", prNumber, 112233, "https://github.com", 4455, $"GenHub-win-x64-PR{prNumber}", DateTime.Now.AddHours(-2), "https://github.com/download", 52 * 1024 * 1024),
            new("1.2.0", "def5678", prNumber, 112232, "https://github.com", 4454, $"GenHub-win-x64-PR{prNumber}", DateTime.Now.AddDays(-1), "https://github.com/download", 51 * 1024 * 1024),
        };
        return Task.FromResult<IReadOnlyList<ArtifactUpdateInfo>>(artifacts);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(["main", "dev", "v1.2-beta", "feature/ui-rework"]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PullRequestInfo>>([
                new PullRequestInfo { Number = 101, Title = "Feature: Enhanced Profile Management", Author = "undead2146", BranchName = "feature/profile-mgmt", State = "open" },
                new PullRequestInfo { Number = 102, Title = "Fix: Application crash on startup", Author = "Bravo15", BranchName = "fix/startup-crash", State = "open" },
                new PullRequestInfo { Number = 105, Title = "Refactor: Move settings to central storage", Author = "GenHubBot", BranchName = "refactor/settings-storage", State = "open" }
            ]);

    /// <inheritdoc/>
    public async Task InstallArtifactAsync(ArtifactUpdateInfo artifactInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Simulate progress
        for (int i = 0; i <= 100; i += 20)
        {
            progress?.Report(new UpdateProgress { PercentComplete = i, Status = $"Installing artifact {artifactInfo.Version}..." });
            await Task.Delay(150, cancellationToken);
        }

        _notificationService?.Show(new NotificationMessage(
            NotificationType.Success,
            "Demo Deployment",
            $"Artifact {artifactInfo.Version} would be installed and the app restarted.",
            5000));
    }

    /// <inheritdoc/>
    public Task InstallPrArtifactAsync(PullRequestInfo prInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public void Uninstall()
    {
    }
}
