using System;
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
public class NotificationService : INotificationService, IDisposable
{
    private readonly ILogger<NotificationService> _logger;
    private readonly Subject<NotificationMessage> _notificationSubject;
    private readonly Subject<Guid> _dismissSubject;
    private readonly Subject<bool> _dismissAllSubject;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        _notificationSubject = new Subject<NotificationMessage>();
        _dismissSubject = new Subject<Guid>();
        _dismissAllSubject = new Subject<bool>();
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
    public void ShowInfo(string title, string message, int? autoDismissMs = null)
    {
        Show(new NotificationMessage(
            NotificationType.Info,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs));
    }

    /// <inheritdoc/>
    public void ShowSuccess(string title, string message, int? autoDismissMs = null)
    {
        Show(new NotificationMessage(
            NotificationType.Success,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs));
    }

    /// <inheritdoc/>
    public void ShowWarning(string title, string message, int? autoDismissMs = null)
    {
        Show(new NotificationMessage(
            NotificationType.Warning,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs));
    }

    /// <inheritdoc/>
    public void ShowError(string title, string message, int? autoDismissMs = null)
    {
        Show(new NotificationMessage(
            NotificationType.Error,
            title,
            message,
            autoDismissMs ?? NotificationConstants.DefaultAutoDismissMs));
    }

    /// <inheritdoc/>
    public void Show(NotificationMessage notification)
    {
        if (_disposed)
        {
            _logger.LogWarning("Attempted to show notification after service disposal");
            return;
        }

        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        _logger.LogDebug(
            "Showing {Type} notification: {Title}",
            notification.Type,
            notification.Title);

        _notificationSubject.OnNext(notification);
    }

    /// <inheritdoc/>
    public void Dismiss(Guid notificationId)
    {
        _logger.LogDebug("Dismiss notification {NotificationId} requested", notificationId);
        _dismissSubject.OnNext(notificationId);
    }

    /// <inheritdoc/>
    public void DismissAll()
    {
        _logger.LogDebug("Dismiss all notifications requested");
        _dismissAllSubject.OnNext(true);
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
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}