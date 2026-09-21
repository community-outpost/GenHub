using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Notifications.Services;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Tests for the sample acquisition notification lifecycle: one persistent
/// overall toast, one green toast per completed download, dismissed at the end.
/// </summary>
public sealed class SampleProjectServiceNotificationTests : IDisposable
{
    private readonly string _tempDirectory;

    public SampleProjectServiceNotificationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_SampleNotif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_ImprovedMenus_EmitsOverallAndPerDownloadToasts()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "ImprovedMenus");
        Directory.CreateDirectory(projectDir);

        using var cacheGuard = SampleCacheGuard.Create(_tempDirectory);
        var notifications = new NotificationService(new Mock<ILogger<NotificationService>>().Object);
        var shown = new List<NotificationMessage>();
        var updates = new List<(Guid Id, string? Title, string Message)>();
        var dismisses = new List<Guid>();
        using var shownSubscription = notifications.Notifications.Subscribe(shown.Add);
        using var updatesSubscription = notifications.UpdateRequests.Subscribe(updates.Add);
        using var dismissSubscription = notifications.DismissRequests.Subscribe(dismisses.Add);

        var service = CreateService(notifications, FakeSuccessfulDownload());

        try
        {
            // Act (unpack of the fake zip is expected to fail; download toasts must still fire)
            await service.EnsureSampleAssetsAsync(projectDir, "ImprovedMenus");

            // Assert: exactly one persistent overall toast, updated and dismissed
            var overall = shown.Where(m => m.Type == NotificationType.Info).Should().ContainSingle().Subject;
            overall.Title.Should().Be("Downloading ImprovedMenus");
            dismisses.Should().Contain(overall.Id);

            // Assert: one green toast for the completed English download
            shown.Where(m => m.Type == NotificationType.Success).Should().ContainSingle()
                .Which.Message.Should().Be("Downloaded Improved Menus English");

            // Assert: overall failure terminal for the failed unpack
            shown.Where(m => m.Type == NotificationType.Error).Should().ContainSingle();

            // Assert: overall toast received live updates (progress callbacks post asynchronously)
            WaitForCondition(() => updates.Any(u => u.Id == overall.Id), "overall toast should receive progress updates");
            updates.Should().Contain(u => u.Id == overall.Id);
        }
        finally
        {
            notifications.Dispose();
        }
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WithCachedAsset_SkipsPerDownloadToast()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "ImprovedMenus");
        Directory.CreateDirectory(projectDir);

        using var cacheGuard = SampleCacheGuard.Create(_tempDirectory);
        cacheGuard.SeedCachedFile("0_ImprovedMenusEnglish.zip", 1_100_000);

        var notifications = new NotificationService(new Mock<ILogger<NotificationService>>().Object);
        var shown = new List<NotificationMessage>();
        using var shownSubscription = notifications.Notifications.Subscribe(shown.Add);

        var mockDownloads = new Mock<IDownloadService>();
        var service = CreateService(notifications, mockDownloads.Object);

        try
        {
            // Act
            await service.EnsureSampleAssetsAsync(projectDir, "ImprovedMenus");

            // Assert: cached asset means no download and no per-download toast,
            // but the overall toast lifecycle still runs.
            mockDownloads.Verify(
                d => d.DownloadFileAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            shown.Should().NotContain(m => m.Type == NotificationType.Success);
            shown.Where(m => m.Type == NotificationType.Info).Should().ContainSingle();
        }
        finally
        {
            notifications.Dispose();
        }
    }

    [Fact]
    public void SampleAcquisitionTracker_ReportDownload_ComputesOverallFraction()
    {
        // Arrange
        var mockNotifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(mockNotifications.Object, "ImprovedMenus");
        var tracker = new SampleProjectService.SampleAcquisitionTracker(scope)
        {
            TotalDownloads = 4,
        };

        // Act
        tracker.ReportDownload("Improved Menus Russian", 1, 0.5);

        // Assert: (1 + 0.5) / 4 = 37%
        mockNotifications.Verify(
            n => n.Update(scope.NotificationId, It.Is<string>(m => m.Contains("37%")), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public void SampleAcquisitionTracker_ReportPhase_KeepsCurrentFraction()
    {
        // Arrange
        var mockNotifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(mockNotifications.Object, "ImprovedMenus");
        var tracker = new SampleProjectService.SampleAcquisitionTracker(scope)
        {
            TotalDownloads = 1,
        };

        // Act
        tracker.ReportDownloadComplete("Improved Menus English", 0);
        tracker.ReportPhase("Extracting...");

        // Assert: completed updates always pass the throttle, so the phase
        // re-report is observable and keeps the 100% fraction with new text.
        mockNotifications.Verify(
            n => n.Update(scope.NotificationId, It.Is<string>(m => m.Contains("100%") && m.Contains("Extracting")), It.IsAny<string?>()),
            Times.Once);
    }

    private static void WaitForCondition(Func<bool> condition, string reason)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            Thread.Sleep(20);
        }

        condition().Should().BeTrue(reason);
    }

    private SampleProjectService CreateService(NotificationService notifications, IDownloadService downloads)
    {
        return new SampleProjectService(
            downloads,
            new CompressedImageToTgaConverter(new Mock<ILogger<CompressedImageToTgaConverter>>().Object),
            new Mock<ILogger<SampleProjectService>>().Object,
            new Mock<IStringTableConversionService>().Object,
            notifications);
    }

    private static IDownloadService FakeSuccessfulDownload()
    {
        var mockDownloads = new Mock<IDownloadService>();
        mockDownloads
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri url, string dest, string? _, IProgress<DownloadProgress>? progress, CancellationToken _) =>
            {
                var bytes = new byte[1_100_000];
                File.WriteAllBytes(dest, bytes);
                progress?.Report(new DownloadProgress(bytes.Length, bytes.Length, Path.GetFileName(dest), url, bytes.Length, TimeSpan.FromSeconds(1)));
                return DownloadResult.CreateSuccess(dest, bytes.Length, TimeSpan.FromSeconds(1));
            });
        return mockDownloads.Object;
    }

    /// <summary>
    /// Stashes pre-existing sample cache zips aside and removes fakes afterwards
    /// so tests never pollute or depend on the real user cache.
    /// </summary>
    private sealed class SampleCacheGuard : IDisposable
    {
        private readonly string _cacheDir;
        private readonly List<(string Original, string Stashed)> _stashed = new();

        private SampleCacheGuard(string cacheDir)
        {
            _cacheDir = cacheDir;
        }

        public static SampleCacheGuard Create(string tempDirectory)
        {
            var stashDir = Path.Combine(tempDirectory, "cache-stash");
            Directory.CreateDirectory(stashDir);
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                ModBuilderConstants.SampleCacheDirName);
            var guard = new SampleCacheGuard(cacheDir);
            if (Directory.Exists(cacheDir))
            {
                foreach (var existing in Directory.GetFiles(cacheDir, "0_ImprovedMenus*.zip"))
                {
                    var stashPath = Path.Combine(stashDir, Path.GetFileName(existing));
                    File.Move(existing, stashPath);
                    guard._stashed.Add((existing, stashPath));
                }
            }

            return guard;
        }

        public void SeedCachedFile(string fileName, int size)
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllBytes(Path.Combine(_cacheDir, fileName), new byte[size]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_cacheDir))
            {
                foreach (var fake in Directory.GetFiles(_cacheDir, "0_ImprovedMenus*.zip"))
                {
                    try
                    {
                        File.Delete(fake);
                    }
                    catch
                    {
                        // Ignore cleanup failures
                    }
                }
            }

            foreach (var (original, stashedPath) in _stashed)
            {
                try
                {
                    File.Move(stashedPath, original);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
    }
}
