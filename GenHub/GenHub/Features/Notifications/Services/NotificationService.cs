using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Notifications.Services;

/// <summary>
/// Service for managing and displaying notifications.
/// </summary>
public class NotificationService(ILogger<NotificationService> logger) : INotificationService, IDisposable
{
    private readonly Subject<NotificationMessage> _notificationSubject = new();
    private readonly Subject<Guid> _dismissSubject = new();
    private readonly Subject<bool> _dismissAllSubject = new();
    private readonly Subject<NotificationMessage> _historySubject = new();
    private readonly List<NotificationMessage> _notificationHistory = new();
    private readonly object _historyLock = new();
    private bool _disposed;

    /// <inheritdoc/>
    public IObservable<NotificationMessage> Notifications => _notificationSubject;

    /// <summary>
    /// Gets the observable stream of dismiss requests.
    /// </summary>
    public IObservable<Guid> DismissRequests => _dismissSubject;

    /// <summary>
    /// Gets the observable stream of dismiss all requests.
    /// </summary>
    public IObservable<bool> DismissAllRequests => _dismissAllSubject;

    /// <inheritdoc/>
    public IObservable<NotificationMessage> NotificationHistory => _historySubject;

    /// <inheritdoc/>
    public void ShowInfo(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
    {
        Show(new NotificationMessage(
            NotificationType.Info,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs,
            showInBadge: showInBadge));
    }

    /// <inheritdoc/>
    public void ShowSuccess(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
    {
        Show(new NotificationMessage(
            NotificationType.Success,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs,
            showInBadge: showInBadge));
    }

    /// <inheritdoc/>
    public void ShowWarning(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
    {
        Show(new NotificationMessage(
            NotificationType.Warning,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs,
            showInBadge: showInBadge));
    }

    /// <inheritdoc/>
    public void ShowError(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
    {
        Show(new NotificationMessage(
            NotificationType.Error,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs,
            showInBadge: showInBadge));
    }

    /// <inheritdoc/>
    public void Show(NotificationMessage notification)
    {
        if (_disposed)
        {
            logger.LogWarning("Attempted to show notification after service disposal");
            return;
        }

        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        logger.LogDebug(
            "Showing {Type} notification: {Title}",
            notification.Type,
            notification.Title);

        // Add to history
        AddToHistory(notification);

        // Emit to both streams
        _notificationSubject.OnNext(notification);
        _historySubject.OnNext(notification);
    }

    /// <inheritdoc/>
    public void Dismiss(Guid notificationId)
    {
        lock (_historyLock)
        {
            var notification = _notificationHistory.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                // Clear action callbacks to prevent memory leaks
                if (notification.Actions != null)
                {
                    foreach (var action in notification.Actions)
                    {
                        action.ClearCallback();
                    }
                }

                // Update history with dismissed status (immutable record)
                var index = _notificationHistory.IndexOf(notification);
                if (index >= 0)
                {
                    _notificationHistory[index] = notification.WithIsDismissed(true);
                }
            }
        }

        logger.LogDebug("Dismiss notification {NotificationId} requested", notificationId);
        _dismissSubject.OnNext(notificationId);
    }

    /// <inheritdoc/>
    public void DismissAll()
    {
        logger.LogDebug("Dismiss all notifications requested");
        _dismissAllSubject.OnNext(true);
    }

    /// <inheritdoc/>
    public void MarkAsRead(Guid notificationId)
    {
        lock (_historyLock)
        {
            var index = _notificationHistory.FindIndex(n => n.Id == notificationId);
            if (index >= 0)
            {
                var notification = _notificationHistory[index];
                _notificationHistory[index] = notification.WithIsRead(true);
                logger.LogDebug("Marked notification {NotificationId} as read", notificationId);
            }
        }
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _notificationHistory.Clear();
            logger.LogDebug("Cleared notification history");
        }
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _notificationSubject?.Dispose();
        _dismissSubject?.Dispose();
        _dismissAllSubject?.Dispose();
        _historySubject?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Adds a notification to the history collection.
    /// </summary>
    /// <param name="notification">The notification to add.</param>
    private void AddToHistory(NotificationMessage notification)
    {
        lock (_historyLock)
        {
            // Remove oldest if at limit
            if (_notificationHistory.Count >= NotificationConstants.MaxHistorySize)
            {
                _notificationHistory.RemoveAt(0);
            }

            _notificationHistory.Add(notification);
        }
    }
}
