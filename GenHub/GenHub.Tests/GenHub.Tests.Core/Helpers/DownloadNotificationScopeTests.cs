using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Moq;
using System;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="DownloadNotificationScope"/>.
/// </summary>
public sealed class DownloadNotificationScopeTests
{
    /// <summary>
    /// Verifies that construction shows the pinned start toast immediately.
    /// </summary>
    [Fact]
    public void Constructor_ShowsPinnedStartToastImmediately()
    {
        var notifications = new Mock<INotificationService>();

        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        notifications.Verify(
            n => n.Show(It.Is<NotificationMessage>(m =>
                m.Id == scope.NotificationId &&
                m.Type == NotificationType.Info &&
                m.Title.Contains("Test Mod", StringComparison.Ordinal) &&
                m.AutoDismissMilliseconds == null)),
            Times.Once);
    }

    /// <summary>
    /// Verifies that completion progress updates the pinned toast with a clamped percentage.
    /// </summary>
    [Fact]
    public void Report_AtCompletion_UpdatesPinnedToastWithClampedPercentage()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        scope.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Downloading,
            ProgressPercentage = 250,
            CurrentOperation = "Downloading files",
        });

        notifications.Verify(
            n => n.Update(
                scope.NotificationId,
                It.Is<string>(message => message.StartsWith("100% - ", StringComparison.Ordinal)),
                It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that an update status with an embedded percentage is shown verbatim without a second prefix.
    /// </summary>
    [Fact]
    public void Report_UpdateProgressWithEmbeddedPercentage_ShowsStatusVerbatim()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "GenHub");

        scope.Report(new UpdateProgress
        {
            Status = "Downloading artifact for PR #547 (ab47b8f)... 24%",
            PercentComplete = 7,
        });

        notifications.Verify(
            n => n.Update(
                scope.NotificationId,
                "Downloading artifact for PR #547 (ab47b8f)... 24%",
                It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that an update status without a percentage keeps the progress prefix.
    /// </summary>
    [Fact]
    public void Report_UpdateProgressWithoutPercentage_KeepsProgressPrefix()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "GenHub");

        scope.Report(new UpdateProgress { Status = "Extracting artifact...", PercentComplete = 30 });

        notifications.Verify(
            n => n.Update(
                scope.NotificationId,
                "30% - Extracting artifact...",
                It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that reports are forwarded to the chained sink even when toast updates throttle.
    /// </summary>
    [Fact]
    public void Report_ForwardsToChainedSink()
    {
        var notifications = new Mock<INotificationService>();
        var chained = new Mock<IProgress<ContentAcquisitionProgress>>();
        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod", null, chained.Object);

        var report = new ContentAcquisitionProgress { ProgressPercentage = 10 };
        scope.Report(report);

        chained.Verify(p => p.Report(report), Times.Once);
    }

    /// <summary>
    /// Verifies that success dismisses the pinned toast and shows exactly one success toast.
    /// </summary>
    [Fact]
    public void CompleteSuccess_DismissesPinnedToastAndShowsSingleSuccessToast()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        scope.CompleteSuccess();
        scope.CompleteSuccess();

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that failure with no error detail falls back to the generic message.
    /// </summary>
    [Fact]
    public void CompleteFailure_WithNullError_UsesGenericMessage()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        scope.CompleteFailure(null);

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowError(
                It.IsAny<string>(),
                DownloadNotificationConstants.UnknownErrorMessage,
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that silent cancellation dismisses without showing a toast.
    /// </summary>
    [Fact]
    public void CompleteCanceled_WhenSilent_DismissesWithoutToast()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        scope.CompleteCanceled(silent: true);

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that disposal without completion dismisses the pinned toast and shows no terminal toast.
    /// </summary>
    [Fact]
    public void Dispose_WithoutCompletion_DismissesPinnedToastWithoutTerminalToast()
    {
        var notifications = new Mock<INotificationService>();
        var scope = new DownloadNotificationScope(notifications.Object, "Test Mod");

        scope.Dispose();

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that completing with a pinned message leaves the toast visible for restart flows.
    /// </summary>
    [Fact]
    public void CompleteWithPinnedMessage_LeavesPinnedToastVisible()
    {
        var notifications = new Mock<INotificationService>();
        var scope = new DownloadNotificationScope(notifications.Object, "GenHub");

        scope.CompleteWithPinnedMessage("Update complete! Restarting...");
        scope.Dispose();

        notifications.Verify(
            n => n.Update(scope.NotificationId, "Update complete! Restarting...", It.IsAny<string>()),
            Times.Once);
        notifications.Verify(n => n.Dismiss(It.IsAny<Guid>()), Times.Never);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that suppressed terminal toasts dismiss the pinned toast without a follow-up toast.
    /// </summary>
    [Fact]
    public void CompleteSuccess_WhenTerminalSuppressed_DismissesWithoutToast()
    {
        var notifications = new Mock<INotificationService>();
        using var scope = new DownloadNotificationScope(
            notifications.Object,
            "Test Mod",
            new DownloadNotificationOptions(ShowTerminalToast: false));

        scope.CompleteSuccess();

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that clearing a pinned message allows a subsequent failure to dismiss the pinned toast and show an error toast.
    /// </summary>
    [Fact]
    public void ClearPinnedMessage_AllowsSubsequentCompleteFailure()
    {
        var notifications = new Mock<INotificationService>();
        var scope = new DownloadNotificationScope(notifications.Object, "GenHub");

        scope.CompleteWithPinnedMessage("Update complete! Restarting...");
        scope.ClearPinnedMessage();
        scope.CompleteFailure("Restart failed");

        notifications.Verify(n => n.Dismiss(scope.NotificationId), Times.Once);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), "Restart failed", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }
}
