using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using System;
using System.Diagnostics;
using System.Globalization;

namespace GenHub.Core.Helpers;

/// <summary>
/// Owns the standardized three-phase download notification lifecycle for a single download.
/// Phase 1 shows a pinned start toast immediately, phase 2 forwards clamped progress to that
/// toast on a throttle, and phase 3 dismisses the pinned toast and shows exactly one terminal
/// toast. Disposal always dismisses the pinned toast so it can never be orphaned.
/// </summary>
public sealed class DownloadNotificationScope : IProgress<ContentAcquisitionProgress>, IDisposable
{
    private readonly INotificationService _notifications;
    private readonly string _contentName;
    private readonly ILocalizationService? _localization;
    private readonly IProgress<ContentAcquisitionProgress>? _chained;
    private readonly bool _showStartToast;
    private readonly bool _showTerminalToast;
    private readonly string? _startTitleOverride;
    private readonly Guid _notificationId = Guid.NewGuid();
    private readonly object _lock = new();
    private long _lastUpdateTimestamp = Stopwatch.GetTimestamp();
    private bool _terminalShown;
    private bool _dismissed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadNotificationScope"/> class
    /// and shows the pinned start toast.
    /// </summary>
    /// <param name="notifications">The notification service.</param>
    /// <param name="contentName">The user-visible content name used in toast titles.</param>
    /// <param name="options">Optional lifecycle overrides.</param>
    /// <param name="chained">Optional progress sink that also receives every clamped report (for example view bindings).</param>
    /// <param name="localization">Optional localization service for toast strings. English fallbacks are used when null.</param>
    public DownloadNotificationScope(
        INotificationService notifications,
        string contentName,
        DownloadNotificationOptions? options = null,
        IProgress<ContentAcquisitionProgress>? chained = null,
        ILocalizationService? localization = null)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _contentName = string.IsNullOrWhiteSpace(contentName) ? ContentConstants.DefaultContentFallbackId : contentName;
        _chained = chained;
        _localization = localization;
        _showStartToast = options?.ShowStartToast ?? true;
        _showTerminalToast = options?.ShowTerminalToast ?? true;
        _startTitleOverride = options?.StartTitle;

        if (_showStartToast)
        {
            var title = _startTitleOverride ?? Localize(
                DownloadNotificationConstants.DownloadingTitleKey,
                DownloadNotificationConstants.DownloadingTitleFormat,
                _contentName);
            var message = options?.StartMessage ?? Localize(
                DownloadNotificationConstants.ConnectingMessageKey,
                DownloadNotificationConstants.ConnectingMessage);
            _notifications.Show(new NotificationMessage(
                NotificationType.Info,
                title,
                message,
                autoDismissMilliseconds: null,
                isPersistent: false,
                showInBadge: false)
            {
                Id = _notificationId,
            });
        }
    }

    /// <summary>
    /// Gets the pinned notification identifier.
    /// </summary>
    public Guid NotificationId => _notificationId;

    /// <summary>
    /// Gets the user-visible content name used in toast titles.
    /// </summary>
    public string ContentName => _contentName;

    private string PinnedTitle => _startTitleOverride ?? Localize(
        DownloadNotificationConstants.DownloadingTitleKey,
        DownloadNotificationConstants.DownloadingTitleFormat,
        _contentName);

    /// <summary>
    /// Reports content acquisition progress to the chained sink and the pinned toast.
    /// </summary>
    /// <param name="value">The progress report.</param>
    public void Report(ContentAcquisitionProgress value)
    {
        if (value == null)
        {
            return;
        }

        var clamped = ClampPercentage(value.ProgressPercentage);
        _chained?.Report(value);

        if (!_showStartToast)
        {
            return;
        }

        var status = value.FormatProgressStatus();
        UpdatePinnedToast(clamped, status);
    }

    /// <summary>
    /// Reports a raw file download progress update to the pinned toast.
    /// </summary>
    /// <param name="value">The download progress report.</param>
    public void Report(DownloadProgress value)
    {
        if (value == null || !_showStartToast)
        {
            return;
        }

        var clamped = ClampPercentage(value.Percentage);
        var status = $"{value.FileName} - {value.FormattedProgress} ({value.FormattedSpeed})";
        UpdatePinnedToast(clamped, status);
    }

    /// <summary>
    /// Reports an application update progress update to the pinned toast.
    /// </summary>
    /// <param name="value">The update progress report.</param>
    public void Report(UpdateProgress value)
    {
        if (value == null || !_showStartToast)
        {
            return;
        }

        var clamped = ClampPercentage(value.PercentComplete);
        var status = $"{clamped:F0}%";
        if (!string.IsNullOrWhiteSpace(value.Message))
        {
            status = value.Message;
        }
        else if (!string.IsNullOrWhiteSpace(value.Status))
        {
            status = value.Status;
        }

        UpdatePinnedToast(clamped, status);
    }

    /// <summary>
    /// Reports fractional progress (0.0 to 1.0) to the pinned toast.
    /// </summary>
    /// <param name="fraction">The completed fraction between 0.0 and 1.0.</param>
    /// <param name="status">Optional status text. The content name is used when null.</param>
    public void ReportFraction(double fraction, string? status = null)
    {
        if (!_showStartToast)
        {
            return;
        }

        UpdatePinnedToast(ClampPercentage(fraction * 100), status ?? _contentName);
    }

    /// <summary>
    /// Dismisses the pinned toast and shows the success terminal toast exactly once.
    /// </summary>
    /// <param name="message">Optional terminal message override. Defaults to the localized completion message.</param>
    /// <param name="title">Optional terminal title override. Defaults to the localized completion title.</param>
    public void CompleteSuccess(string? message = null, string? title = null)
    {
        lock (_lock)
        {
            if (_terminalShown)
            {
                return;
            }

            _terminalShown = true;
            DismissPinnedToastLocked();

            if (!_showTerminalToast)
            {
                return;
            }

            var successTitle = title ?? Localize(DownloadNotificationConstants.CompleteTitleKey, DownloadNotificationConstants.CompleteTitle);
            var successMessage = message ?? Localize(DownloadNotificationConstants.CompleteMessageKey, DownloadNotificationConstants.CompleteMessageFormat, _contentName);
            _notifications.ShowSuccess(successTitle, successMessage, NotificationDurations.Medium);
        }
    }

    /// <summary>
    /// Dismisses the pinned toast and shows the failure terminal toast exactly once.
    /// </summary>
    /// <param name="errorMessage">The failure detail. A generic message is used when null.</param>
    /// <param name="title">Optional terminal title override. Defaults to the localized failure title.</param>
    public void CompleteFailure(string? errorMessage, string? title = null)
    {
        lock (_lock)
        {
            if (_terminalShown)
            {
                return;
            }

            _terminalShown = true;
            DismissPinnedToastLocked();

            if (!_showTerminalToast)
            {
                return;
            }

            var failureTitle = title ?? Localize(DownloadNotificationConstants.FailedTitleKey, DownloadNotificationConstants.FailedTitleFormat, _contentName);
            var failureMessage = errorMessage;
            if (string.IsNullOrWhiteSpace(failureMessage))
            {
                failureMessage = Localize(DownloadNotificationConstants.UnknownErrorKey, DownloadNotificationConstants.UnknownErrorMessage);
            }

            _notifications.ShowError(failureTitle, failureMessage, NotificationDurations.Medium);
        }
    }

    /// <summary>
    /// Dismisses the pinned toast and shows the cancellation terminal toast exactly once.
    /// </summary>
    /// <param name="silent">When true, dismisses without showing the cancellation toast (for shutdown paths).</param>
    public void CompleteCanceled(bool silent = false)
    {
        lock (_lock)
        {
            if (_terminalShown)
            {
                return;
            }

            _terminalShown = true;
            DismissPinnedToastLocked();

            if (!_showTerminalToast || silent)
            {
                return;
            }

            _notifications.ShowInfo(
                Localize(
                    DownloadNotificationConstants.CanceledTitleKey,
                    DownloadNotificationConstants.CanceledTitle),
                Localize(
                    DownloadNotificationConstants.CanceledMessageKey,
                    DownloadNotificationConstants.CanceledMessageFormat,
                    _contentName),
                NotificationDurations.Short);
        }
    }

    /// <summary>
    /// Marks the operation complete with a final pinned message and no terminal toast.
    /// The pinned toast is left visible for restart flows and disposal will not dismiss it.
    /// </summary>
    /// <param name="finalMessage">The final pinned message.</param>
    public void CompleteWithPinnedMessage(string finalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalMessage);

        lock (_lock)
        {
            if (_terminalShown || _dismissed)
            {
                return;
            }

            _terminalShown = true;
            _dismissed = true;

            if (!_showStartToast)
            {
                return;
            }

            _notifications.Update(_notificationId, finalMessage, PinnedTitle);
        }
    }

    /// <summary>
    /// Clears a previous pinned completion so that a subsequent terminal failure or cancellation
    /// can dismiss the pinned toast and display its terminal notification.
    /// </summary>
    public void ClearPinnedMessage()
    {
        lock (_lock)
        {
            if (_terminalShown)
            {
                _terminalShown = false;
                _dismissed = false;
            }
        }
    }

    /// <summary>
    /// Dismisses the pinned toast without showing a terminal toast.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DismissPinnedToastLocked();
        }

        GC.SuppressFinalize(this);
    }

    private static double ClampPercentage(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private void UpdatePinnedToast(double clampedPercentage, string status)
    {
        var title = PinnedTitle;

        lock (_lock)
        {
            if (_terminalShown || _dismissed)
            {
                return;
            }

            var elapsedMs = Stopwatch.GetElapsedTime(_lastUpdateTimestamp).TotalMilliseconds;
            var isComplete = clampedPercentage >= 100;
            if (elapsedMs < ManifestConstants.NotificationUpdateThrottleMs && !isComplete)
            {
                return;
            }

            _lastUpdateTimestamp = Stopwatch.GetTimestamp();

            var message = Localize(
                DownloadNotificationConstants.ProgressMessageKey,
                DownloadNotificationConstants.ProgressMessageFormat,
                ((int)clampedPercentage).ToString(CultureInfo.InvariantCulture),
                status);
            _notifications.Update(_notificationId, message, title);
        }
    }

    private void DismissPinnedToastLocked()
    {
        if (!_showStartToast || _dismissed)
        {
            return;
        }

        _dismissed = true;
        _notifications.Dismiss(_notificationId);
    }

    private string Localize(string key, string fallback, params object?[] args)
    {
        if (_localization != null && _localization.TryGetString(key, out var localized, args))
        {
            return localized;
        }

        return args.Length == 0
            ? fallback
            : string.Format(CultureInfo.InvariantCulture, fallback, args);
    }
}
