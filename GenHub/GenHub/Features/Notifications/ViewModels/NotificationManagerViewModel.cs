using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// Centralized ViewModel for managing all notification toasts.
/// </summary>
public class NotificationManagerViewModel : ViewModelBase, IDisposable
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationManagerViewModel> _logger;
    private readonly ILogger<NotificationItemViewModel> _itemLogger;
    private readonly IDisposable _notificationSubscription;
    private readonly IDisposable _dismissSubscription;
    private readonly IDisposable _dismissAllSubscription;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the collection of active notifications.
    /// </summary>
    public ObservableCollection<NotificationItemViewModel> ActiveNotifications { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationManagerViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="itemLogger">The logger for notification items.</param>
    public NotificationManagerViewModel(
        INotificationService notificationService,
        ILogger<NotificationManagerViewModel> logger,
        ILogger<NotificationItemViewModel> itemLogger)
    {
        _notificationService = notificationService;
        _logger = logger;
        _itemLogger = itemLogger;

        ActiveNotifications = [];

        _notificationSubscription = _notificationService.Notifications.Subscribe(HandleNotificationReceived);
        _dismissSubscription = _notificationService.DismissRequests.Subscribe(HandleDismissRequest);
        _dismissAllSubscription = _notificationService.DismissAllRequests.Subscribe(_ => HandleDismissAllRequest());

        _logger.LogInformation("NotificationManagerViewModel initialized");
    }

    /// <summary>
    /// Adds a new notification to the active collection.
    /// </summary>
    /// <param name="message">The notification message.</param>
    public void AddNotification(
        NotificationMessage message)
    {
        if (_disposed)
        {
            _logger.LogWarning("Attempted to add notification after disposal");
            return;
        }

        // Use InvokeAsync to ensure we're on UI thread and handle exceptions
        Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                try
                {
                    lock (_lock)
                    {
                        var viewModel = new NotificationItemViewModel(message, RemoveNotification, _itemLogger);
                        ActiveNotifications.Insert(0, viewModel);

                        _logger.LogDebug(
                            "Added {Type} notification: {Title} (Total: {Count})",
                            message.Type,
                            message.Title,
                            ActiveNotifications.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding notification");
                }
            },
            DispatcherPriority.Send);
    }

    /// <summary>
    /// Removes a notification from the active collection.
    /// </summary>
    /// <param name="id">The notification ID.</param>
    public void RemoveNotification(Guid id)
    {
        Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                try
                {
                    lock (_lock)
                    {
                        var notification = ActiveNotifications.FirstOrDefault(n => n.Id == id);
                        if (notification != null)
                        {
                            ActiveNotifications.Remove(notification);
                            notification.Dispose();

                            _logger.LogDebug("Removed notification {NotificationId}", id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing notification");
                }
            },
            DispatcherPriority.Send);
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _notificationSubscription?.Dispose();
        _dismissSubscription?.Dispose();
        _dismissAllSubscription?.Dispose();

        foreach (var notification in ActiveNotifications)
        {
            notification?.Dispose();
        }

        ActiveNotifications.Clear();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void HandleNotificationReceived(NotificationMessage message)
    {
        _logger.LogDebug("Notification received: {Title}", message.Title);
        AddNotification(message);
    }

    private void HandleDismissRequest(Guid notificationId)
    {
        _logger.LogDebug("Dismiss request received for notification {NotificationId}", notificationId);
        RemoveNotification(notificationId);
    }

    private void HandleDismissAllRequest()
    {
        _logger.LogDebug("Dismiss all request received");
        Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                try
                {
                    lock (_lock)
                    {
                        foreach (var notification in ActiveNotifications)
                        {
                            notification?.Dispose();
                        }

                        ActiveNotifications.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error dismissing all notifications");
                }
            },
            DispatcherPriority.Send);
    }
}