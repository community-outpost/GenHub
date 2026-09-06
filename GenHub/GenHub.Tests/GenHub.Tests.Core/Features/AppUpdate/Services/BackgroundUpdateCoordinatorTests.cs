using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Contains unit tests for the <see cref="BackgroundUpdateCoordinator"/> class.
/// </summary>
public class BackgroundUpdateCoordinatorTests
{
    /// <summary>
    /// Verifies that when startup update check is disabled, no update checks are performed on initialize.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenStartupCheckDisabled_DoesNotCheckUpdatesAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings { AutoCheckForUpdatesOnStartup = false, AutoCheckForUpdatesPeriodically = false });
        var mockNotificationService = CreateNotificationServiceMock();
        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.InitializeAsync();

        mockVelopack.Verify(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockVelopack.Verify(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when a subscribed PR is merged or closed, update checking falls back to the development branch artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenPrIsMerged_FallsBackToDevelopmentArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            AutoCheckForUpdatesOnStartup = true,
            SubscribedPrNumber = 265,
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-development",
            GitHash: "abc1234",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 265);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, null);

        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                if (mockVelopack.Object.SubscribedPrNumber == 265)
                {
                    mockVelopack.SetupGet(x => x.IsPrMergedOrClosed).Returns(true);
                    return Task.FromResult<ArtifactUpdateInfo?>(null);
                }

                if (mockVelopack.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.True(updateNotification.IsPersistent);
        Assert.True(updateNotification.ShowInBadge);
        Assert.Contains("265", updateNotification.Message);
        Assert.Single(updateNotification.Actions);
    }

    /// <summary>
    /// Verifies that when a subscribed custom branch has no artifacts, update checking falls back to the development branch artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenCustomBranchIsStale_FallsBackToDevelopmentArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            AutoCheckForUpdatesOnStartup = true,
            SubscribedBranch = "feat/deleted-branch",
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-development",
            GitHash: "abc1234",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, null);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, "feat/deleted-branch");

        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                if (mockVelopack.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.True(updateNotification.IsPersistent);
        Assert.True(updateNotification.ShowInBadge);
        Assert.Contains("feat/deleted-branch", updateNotification.Message);
        Assert.Single(updateNotification.Actions);
    }

    /// <summary>
    /// Verifies that when a custom branch has no artifacts and releases are checked via GitHub API, fallback notification is displayed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenCustomBranchIsStaleAndNoArtifact_FallsBackToGitHubApiReleaseAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            SubscribedBranch = "feat/deleted-branch",
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, null);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, "feat/deleted-branch");
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactUpdateInfo?)null);
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Velopack.UpdateInfo?)null);
        mockVelopack.SetupGet(x => x.HasUpdateAvailableFromGitHub).Returns(true);
        mockVelopack.SetupGet(x => x.LatestVersionFromGitHub).Returns("1.5.0");

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Equal(AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle, updateNotification.Title);
        Assert.Contains("1.5.0", updateNotification.Message);
    }

    /// <summary>
    /// Verifies that a token-authenticated main subscription notifies from the release fallback when no branch artifact exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenMainBranchHasNoArtifact_NotifiesFromStandardReleaseFallbackAsync()
    {
        var notificationShown = new TaskCompletionSource<NotificationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new UserSettings { SubscribedBranch = AppUpdateConstants.MainBranch };
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(settings);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, null);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, AppUpdateConstants.MainBranch);
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactUpdateInfo?)null);
        var stableRelease = new Velopack.VelopackAsset
        {
            PackageId = "GenHub",
            Version = NuGet.Versioning.NuGetVersion.Parse("1.5.0"),
            Type = Velopack.VelopackAssetType.Full,
            FileName = "GenHub-1.5.0-full.nupkg",
            SHA1 = "0000000000000000000000000000000000000000",
            Size = 1024,
        };
        var stableUpdate = new Velopack.UpdateInfo(stableRelease, false, null, []);
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stableUpdate);
        mockVelopack.SetupGet(x => x.HasUpdateAvailableFromGitHub).Returns(false);

        var mockTokenStorage = new Mock<IGitHubTokenStorage>();
        mockTokenStorage.Setup(x => x.HasToken()).Returns(true);
        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(message => notificationShown.TrySetResult(message));

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            new Mock<ILogger<BackgroundUpdateCoordinator>>().Object,
            mockTokenStorage.Object);

        await coordinator.CheckForUpdatesAsync();
        var notification = await notificationShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        mockVelopack.Verify(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockVelopack.Verify(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AppUpdateConstants.UpdateAvailableNotificationTitle, notification.Title);
        Assert.Contains("1.5.0", notification.Message);
        Assert.Single(notification.Actions);
        Assert.True(notification.IsPersistent);
        Assert.True(notification.ShowInBadge);
    }

    /// <summary>
    /// Verifies that receiving an update settings changed message restarts the periodic timer without exception.
    /// </summary>
    [Fact]
    public void Receive_UpdateSettingsChangedMessage_RestartsPeriodicTimerWithoutException()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockNotificationService = CreateNotificationServiceMock();
        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        var message = new UpdateSettingsChangedMessage(
            AutoCheckForUpdatesOnStartup: true,
            AutoCheckForUpdatesPeriodically: true,
            PeriodicUpdateCheckIntervalMinutes: 15);

        var exception = Record.Exception(() => coordinator.Receive(message));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that when an artifact update is available, the notification action installs the artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenArtifactUpdateAvailable_NotificationActionInstallsArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            AutoCheckForUpdatesOnStartup = true,
            SubscribedPrNumber = 100,
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-pr100",
            GitHash: "abc1234",
            PullRequestNumber: 100,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 100);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, null);
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifactInfo);

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.PrUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Single(updateNotification.Actions);

        // Execute the action to verify install
        updateNotification.Actions[0].Callback?.Invoke();

        mockVelopack.Verify(
            x => x.InstallArtifactAsync(artifactInfo, It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a persistent update notification cannot start installation after the coordinator is disposed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateNotificationAction_WhenCoordinatorDisposed_DoesNotInstallArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();
        var settings = new UserSettings { SubscribedPrNumber = 100 };
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(settings);

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-pr100",
            GitHash: "abc1234",
            PullRequestNumber: 100,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 100);
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifactInfo);

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(message => notificationShownTcs.TrySetResult(message));

        var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            new Mock<ILogger<BackgroundUpdateCoordinator>>().Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Dispose();

        updateNotification.Actions[0].Callback?.Invoke();

        mockVelopack.Verify(
            x => x.InstallArtifactAsync(It.IsAny<ArtifactUpdateInfo>(), It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockNotificationService.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that disposing the coordinator cancels an update installation without showing an error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateNotificationAction_WhenDisposedDuringInstall_CancelsWithoutErrorAsync()
    {
        var updateNotificationShown = new TaskCompletionSource<NotificationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressDismissed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new UserSettings { SubscribedPrNumber = 100 };
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(settings);

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-pr100",
            GitHash: "abc1234",
            PullRequestNumber: 100,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 100);
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifactInfo);
        mockVelopack.Setup(x => x.InstallArtifactAsync(
                artifactInfo,
                It.IsAny<IProgress<UpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns<ArtifactUpdateInfo, IProgress<UpdateProgress>?, CancellationToken>(async (_, _, token) =>
            {
                installStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    installCancelled.TrySetResult(true);
                    throw;
                }
            });

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(message =>
            {
                if (message.Title == AppUpdateConstants.PrUpdateAvailableNotificationTitle)
                {
                    updateNotificationShown.TrySetResult(message);
                }
            });
        mockNotificationService.Setup(x => x.Dismiss(It.IsAny<Guid>()))
            .Callback(() => progressDismissed.TrySetResult(true));

        var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            new Mock<ILogger<BackgroundUpdateCoordinator>>().Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await updateNotificationShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        updateNotification.Actions[0].Callback?.Invoke();
        await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Dispose();

        await installCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await progressDismissed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mockNotificationService.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that checking updates repeatedly with the same version deduplicates notifications.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenSameUpdateCheckedRepeatedly_DeduplicatesNotificationAsync()
    {
        var showCount = 0;

        var userSettings = new UserSettings
        {
            SubscribedPrNumber = 100,
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var artifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-pr100",
            GitHash: "abc1234",
            PullRequestNumber: 100,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 100);
        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifactInfo);

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(_ => Interlocked.Increment(ref showCount));

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        await coordinator.CheckForUpdatesAsync();

        Assert.Equal(1, showCount);
    }

    /// <summary>
    /// Verifies that cancellation token propagation cancels update checks.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockNotificationService = CreateNotificationServiceMock();
        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.CheckForUpdatesAsync(cts.Token));
    }

    /// <summary>
    /// Verifies that disposing the coordinator cancels a direct update check and suppresses notifications.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenDisposedDuringCheck_CancelsWithoutNotificationAsync()
    {
        var checkStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                checkStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    checkCancelled.TrySetResult(true);
                    throw;
                }

                return null;
            });

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockNotificationService = CreateNotificationServiceMock();
        var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            new Mock<ILogger<BackgroundUpdateCoordinator>>().Object);

        var checkTask = coordinator.CheckForUpdatesAsync();
        await checkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkTask);
        await checkCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mockNotificationService.Verify(x => x.Show(It.IsAny<NotificationMessage>()), Times.Never);
    }

    /// <summary>
    /// Verifies that executing the PR merged fallback notification action clears the subscribed PR setting and installs the dev artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenPrMerged_NotificationActionClearsSubscriptionAndInstallsDevArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            AutoCheckForUpdatesOnStartup = true,
            SubscribedPrNumber = 265,
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        mockUserSettings.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(userSettings));

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-development",
            GitHash: "abc1234",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 265);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, null);

        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                if (mockVelopack.Object.SubscribedPrNumber == 265)
                {
                    mockVelopack.SetupGet(x => x.IsPrMergedOrClosed).Returns(true);
                    return Task.FromResult<ArtifactUpdateInfo?>(null);
                }

                if (mockVelopack.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Single(updateNotification.Actions);

        // Execute action
        updateNotification.Actions[0].Callback?.Invoke();

        // Wait brief delay for async install and settings update
        await Task.Delay(100);

        mockVelopack.Verify(
            x => x.InstallArtifactAsync(devArtifactInfo, It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Null(userSettings.SubscribedPrNumber);
        mockUserSettings.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that executing the stale branch fallback notification action clears the subscribed branch setting and installs the dev artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenCustomBranchStale_NotificationActionClearsSubscriptionAndInstallsDevArtifactAsync()
    {
        var notificationShownTcs = new TaskCompletionSource<NotificationMessage>();

        var userSettings = new UserSettings
        {
            AutoCheckForUpdatesOnStartup = true,
            SubscribedBranch = "feat/old-branch",
            DismissedUpdateVersion = null,
        };

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        mockUserSettings.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(userSettings));

        var devArtifactInfo = new ArtifactUpdateInfo(
            Version: "0.0.99999-development",
            GitHash: "abc1234",
            PullRequestNumber: null,
            WorkflowRunId: 12345,
            WorkflowRunUrl: "https://example.com/runs/1",
            ArtifactId: 67890,
            ArtifactName: "genhub-velopack-linux-0.0.99999",
            CreatedAt: DateTime.UtcNow,
            DownloadUrl: "https://example.com/artifact.zip",
            Size: 1024);

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, null);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, "feat/old-branch");

        mockVelopack.Setup(x => x.CheckForArtifactUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                if (mockVelopack.Object.SubscribedBranch == AppUpdateConstants.DevelopmentBranch)
                {
                    return Task.FromResult<ArtifactUpdateInfo?>(devArtifactInfo);
                }

                return Task.FromResult<ArtifactUpdateInfo?>(null);
            });

        var mockNotificationService = CreateNotificationServiceMock();
        mockNotificationService.Setup(x => x.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg =>
            {
                if (msg.Title == AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle)
                {
                    notificationShownTcs.TrySetResult(msg);
                }
            });

        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        using var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        await coordinator.CheckForUpdatesAsync();
        var updateNotification = await notificationShownTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(updateNotification);
        Assert.Single(updateNotification.Actions);

        // Execute action
        updateNotification.Actions[0].Callback?.Invoke();

        await Task.Delay(100);

        mockVelopack.Verify(
            x => x.InstallArtifactAsync(devArtifactInfo, It.IsAny<IProgress<UpdateProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Null(userSettings.SubscribedBranch);
        mockUserSettings.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that Dispose can be safely called multiple times.
    /// </summary>
    [Fact]
    public void Dispose_CanBeCalledMultipleTimesWithoutThrowing()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());
        var mockNotificationService = CreateNotificationServiceMock();
        var mockLogger = new Mock<ILogger<BackgroundUpdateCoordinator>>();

        var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        var exception = Record.Exception(() =>
        {
            coordinator.Dispose();
            coordinator.Dispose();
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that disposing the coordinator cancels a check started by its periodic timer.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PeriodicUpdateTimer_WhenCoordinatorDisposed_CancelsInFlightCheckAsync()
    {
        var checkStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                checkStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    checkCancelled.TrySetResult(true);
                    throw;
                }

                return null;
            });

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings
        {
            AutoCheckForUpdatesOnStartup = false,
            AutoCheckForUpdatesPeriodically = true,
            PeriodicUpdateCheckIntervalMinutes = 15,
        });
        var mockNotificationService = CreateNotificationServiceMock();

        var coordinator = new BackgroundUpdateCoordinator(
            mockVelopack.Object,
            mockUserSettings.Object,
            mockNotificationService.Object,
            new Mock<ILogger<BackgroundUpdateCoordinator>>().Object);
        await coordinator.InitializeAsync();

        var timerField = typeof(BackgroundUpdateCoordinator).GetField(
            "_periodicUpdateTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(timerField);
        var timer = Assert.IsType<Timer>(timerField.GetValue(coordinator));
        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        await checkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Dispose();

        await checkCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mockNotificationService.Verify(x => x.Show(It.IsAny<NotificationMessage>()), Times.Never);
    }

    private static Mock<INotificationService> CreateNotificationServiceMock()
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(x => x.Notifications).Returns(Observable.Empty<NotificationMessage>());
        mock.Setup(x => x.NotificationHistory).Returns(Observable.Empty<NotificationMessage>());
        mock.Setup(x => x.DismissRequests).Returns(Observable.Empty<Guid>());
        mock.Setup(x => x.DismissAllRequests).Returns(Observable.Empty<bool>());
        mock.Setup(x => x.UpdateRequests).Returns(Observable.Empty<(Guid Id, string? Title, string Message)>());
        return mock;
    }
}
