using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.Services;

/// <summary>
/// Verifies the standardized notification lifecycle owned by <see cref="ContentDownloadCoordinator"/>.
/// </summary>
public sealed class ContentDownloadCoordinatorNotificationTests
{
    private readonly Mock<IContentOrchestrator> _orchestrator;
    private readonly Mock<IContentStateService> _stateService;
    private readonly Mock<INotificationService> _notifications;
    private readonly ContentDownloadCoordinator _coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDownloadCoordinatorNotificationTests"/> class.
    /// </summary>
    public ContentDownloadCoordinatorNotificationTests()
    {
        _orchestrator = new Mock<IContentOrchestrator>();
        _stateService = new Mock<IContentStateService>();
        _notifications = new Mock<INotificationService>();
        _coordinator = new ContentDownloadCoordinator(
            _orchestrator.Object,
            _stateService.Object,
            _notifications.Object,
            NullLogger<ContentDownloadCoordinator>.Instance);
    }

    /// <summary>
    /// Verifies that a successful download shows the pinned start toast and exactly one success toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_OnSuccess_ShowsStartAndSingleSuccessToastAsync()
    {
        var manifest = CreateManifest();
        _orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var result = await _coordinator.DownloadContentAsync(CreateSearchResult());

        Assert.True(result.Success);
        _notifications.Verify(
            n => n.Show(It.Is<NotificationMessage>(m => m.AutoDismissMilliseconds == null)),
            Times.Once);
        _notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a failed download shows the pinned start toast and exactly one error toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_OnFailure_ShowsStartAndSingleErrorToastAsync()
    {
        _orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("boom"));

        var result = await _coordinator.DownloadContentAsync(CreateSearchResult());

        Assert.False(result.Success);
        _notifications.Verify(
            n => n.Show(It.Is<NotificationMessage>(m => m.AutoDismissMilliseconds == null)),
            Times.Once);
        _notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that suppressed downloads show no toasts at all for bundle aggregation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_WhenSuppressNotifications_ShowsNoToastsAsync()
    {
        var manifest = CreateManifest();
        _orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var result = await _coordinator.DownloadContentAsync(CreateSearchResult(), null, CancellationToken.None, suppressNotifications: true);

        Assert.True(result.Success);
        _notifications.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.Never);
        _notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _notifications.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that cancelling the only waiter dismisses the pinned toast with a cancel notice.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_WhenCallerCancels_ShowsCancelToastAsync()
    {
        var acquisitionTcs = new TaskCompletionSource<OperationResult<ContentManifest>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var internalToken = CancellationToken.None;
        _orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((_, _, ct) => internalToken = ct)
            .Returns(acquisitionTcs.Task);

        using var cts = new CancellationTokenSource();
        var downloadTask = _coordinator.DownloadContentAsync(CreateSearchResult(), null, cts.Token);
        await WaitForConditionAsync(() => internalToken.CanBeCanceled, "acquisition to start");
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);

        acquisitionTcs.TrySetCanceled(internalToken);
        await WaitForConditionAsync(
            () => _notifications.Invocations.Count(i => i.Method.Name == nameof(INotificationService.ShowInfo)) == 1,
            "cancel toast to appear");

        _notifications.Verify(n => n.Dismiss(It.IsAny<Guid>()), Times.Once);
        _notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    private static ContentSearchResult CreateSearchResult()
    {
        return new ContentSearchResult
        {
            Id = "test.mod.toast",
            Name = "Test Mod",
            Version = "1.0",
            ProviderName = "TestRealm",
        };
    }

    private static ContentManifest CreateManifest()
    {
        return new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.mod.toast"),
            Name = "Test Mod",
            ContentType = ContentType.Addon,
        };
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(20);
        }
    }
}
