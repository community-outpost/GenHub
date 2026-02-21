using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Notifications.Services;

/// <summary>
/// Service for managing and displaying notifications.
/// </summary>
public class NotificationService : INotificationService, IDisposable
{
    private readonly ILogger<NotificationService> _logger;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly Subject<NotificationMessage> _notificationSubject = new();
    private readonly Subject<Guid> _dismissSubject = new();
    private readonly Subject<bool> _dismissAllSubject = new();
    private readonly Subject<NotificationMessage> _historySubject = new();
    private readonly List<NotificationMessage> _notificationHistory = new();
    private readonly object _historyLock = new();
    private readonly object _muteLock = new();
    private NotificationMuteState _muteState = NotificationMuteState.None;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationService"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger used to record diagnostic and operational information.
    /// </param>
    /// <param name="userSettingsService">
    /// The user settings service used to load and persist notification mute state.
    /// May be <see langword="null"/> if persistent mute state is not supported.
    /// </param>
    public NotificationService(
        ILogger<NotificationService> logger,
        IUserSettingsService? userSettingsService = null)
    {
        _logger = logger;
        _userSettingsService = userSettingsService;

        if (_userSettingsService != null)
        {
            var settings = _userSettingsService.Get();
            if (settings.IsNotificationMuted)
            {
                _muteState = NotificationMuteState.Persistent;
                _logger.LogDebug("Loaded persistent notification mute state from settings");
            }
        }
    }

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
    public NotificationMuteState MuteState
    {
        get
        {
            lock (_muteLock)
            {
                return _muteState;
            }
        }
    }

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
            _logger.LogWarning("Attempted to show notification after service disposal");
            return;
        }

        ArgumentNullException.ThrowIfNull(notification);

        bool muted;
        NotificationMuteState state;
        lock (_muteLock)
        {
            state = _muteState;
            muted = state != NotificationMuteState.None;
        }

        if (muted)
        {
            _logger.LogDebug(
                "Notification muted ({MuteState}), adding to history only: {Title}",
                state,
                notification.Title);
        }
        else
        {
            _logger.LogDebug(
                "Showing {Type} notification: {Title}",
                notification.Type,
                notification.Title);
        }

        // Always add to history so feed shows it when user opens
        AddToHistory(notification);
        _historySubject.OnNext(notification);

        // Only emit to live notifications stream when not muted
        if (!muted)
        {
            _notificationSubject.OnNext(notification);
        }
    }

    /// <inheritdoc/>
    public async Task MuteSession(CancellationToken cancellationToken = default)
    {
        lock (_muteLock)
        {
            _muteState = NotificationMuteState.Session;
        }

        if (_userSettingsService != null)
        {
            _userSettingsService.Update(s => s.IsNotificationMuted = false);
            await _userSettingsService.SaveAsync(cancellationToken);
        }

        _logger.LogInformation("Notifications muted for current session");
    }

    /// <inheritdoc/>
    public async Task MutePersistent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_muteLock)
        {
            _muteState = NotificationMuteState.Persistent;
        }

        if (_userSettingsService != null)
        {
            _userSettingsService.Update(s => s.IsNotificationMuted = true);
            await _userSettingsService.SaveAsync(cancellationToken);
        }

        _logger.LogInformation("Notifications muted persistently");
    }

    /// <inheritdoc/>
    public async Task Unmute(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_muteLock)
        {
            _muteState = NotificationMuteState.None;
        }

        if (_userSettingsService != null)
        {
            _userSettingsService.Update(s => s.IsNotificationMuted = false);
            await _userSettingsService.SaveAsync(cancellationToken);
        }

        _logger.LogInformation("Notifications unmuted");
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

        _logger.LogDebug("Dismiss notification {NotificationId} requested", notificationId);
        _dismissSubject.OnNext(notificationId);
    }

    /// <inheritdoc/>
    public void DismissAll()
    {
        _logger.LogDebug("Dismiss all notifications requested");
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
                _logger.LogDebug("Marked notification {NotificationId} as read", notificationId);
            }
        }
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _notificationHistory.Clear();
            _logger.LogDebug("Cleared notification history");
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
