using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1202, SA1507, SA1508

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// ViewModel for managing notification feed and history.
/// </summary>
public partial class NotificationFeedViewModel : ViewModelBase, IDisposable
{
    private readonly INotificationService _notificationService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<NotificationFeedViewModel> _logger;
    private readonly IDisposable _historySubscription;
    private readonly object _stateLock = new();
    private bool _disposed;

    [ObservableProperty]
    private bool _isFeedOpen;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadNotifications))]
    private int _badgeCount;

    /// <summary>
    /// Gets a value indicating whether there are unread notifications that should be shown in the badge.
    /// </summary>
    public bool HasUnreadNotifications => BadgeCount > 0;

    /// <summary>
    /// Gets the collection of notification history items.
    /// </summary>
    public ObservableCollection<NotificationFeedItemViewModel> NotificationHistory { get; }

    /// <summary>
    /// Gets a value indicating whether there are any notifications.
    /// </summary>
    public bool HasNotifications => NotificationHistory?.Any() == true;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationFeedViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="logger">The logger instance.</param>
    public NotificationFeedViewModel(
        INotificationService notificationService,
        ILoggerFactory loggerFactory,
        ILogger<NotificationFeedViewModel> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger;

        NotificationHistory = [];
        UnreadCount = 0;

        // Subscribe to notification history
        _historySubscription = notificationService.NotificationHistory.Subscribe(OnNotificationAdded);

        _logger.LogInformation("NotificationFeedViewModel initialized");
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _historySubscription?.Dispose();

        foreach (var item in NotificationHistory)
        {
            item?.Dispose();
        }

        NotificationHistory.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Adds a notification to the feed.
    /// </summary>
    /// <param name="message">The notification message.</param>
    public void AddNotification(NotificationMessage message)
    {
        if (_disposed)
        {
            _logger.LogWarning("Attempted to add notification after disposal");
            return;
        }

        RunOnUI(() =>
        {
            lock (_stateLock)
            {
                var feedItem = new NotificationFeedItemViewModel(
                    message,
                    MarkAsRead,
                    DismissNotification,
                    _loggerFactory.CreateLogger<NotificationFeedItemViewModel>());

                NotificationHistory.Insert(0, feedItem);

                if (!message.IsRead)
                {
                    UnreadCount++;

                    // Only increment badge count if ShowInBadge is true
                    if (message.ShowInBadge)
                    {
                        BadgeCount++;
                    }
                }

                OnPropertyChanged(nameof(HasNotifications));
            }
        });

        _logger.LogDebug(
            "Added notification to feed: {Title} (Unread: {UnreadCount}, Badge: {BadgeCount})",
            message.Title,
            UnreadCount,
            BadgeCount);
    }

    /// <summary>
    /// Executes an action on the UI thread.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    protected virtual void RunOnUI(Action action)
    {
        Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>
    /// Toggles the notification feed visibility.
    /// When opening the feed, resets the badge count.
    /// </summary>
    [RelayCommand]
    private void ToggleFeed()
    {
        _logger.LogInformation("ToggleFeed command executed! Current state: {IsFeedOpen}", IsFeedOpen);

        IsFeedOpen = !IsFeedOpen;

        // Reset badge count when opening the feed
        if (IsFeedOpen)
        {
            BadgeCount = 0;
            _logger.LogInformation("Feed opened, badge count reset to 0");
        }
        else
        {
            _logger.LogInformation("Feed closed");
        }

        _logger.LogInformation("Feed toggled: {IsOpen}", IsFeedOpen);
    }

    /// <summary>
    /// Clears all notifications from the history.
    /// </summary>
    [RelayCommand]
    private void ClearAll()
    {
        _notificationService.ClearHistory();

        RunOnUI(() =>
        {
            lock (_stateLock)
            {
                NotificationHistory.Clear();
                UnreadCount = 0;
                OnPropertyChanged(nameof(HasNotifications));
            }
        });

        _logger.LogInformation("Cleared all notifications from feed");
    }

    /// <summary>
    /// Dismisses a specific notification from the feed.
    /// </summary>
    /// <param name="id">The notification ID.</param>
    [RelayCommand]
    private void DismissNotification(Guid id)
    {
        _notificationService.Dismiss(id);

        RunOnUI(() =>
        {
            lock (_stateLock)
            {
                var item = NotificationHistory.FirstOrDefault(n => n.Id == id);
                if (item != null)
                {
                    NotificationHistory.Remove(item);
                    UpdateUnreadCount();
                    OnPropertyChanged(nameof(HasNotifications));
                }
            }
        });

        _logger.LogDebug("Dismissed notification {NotificationId}", id);
    }

    /// <summary>
    /// Marks a notification as read.
    /// </summary>
    /// <param name="id">The notification ID.</param>
    [RelayCommand]
    private void MarkAsRead(Guid id)
    {
        _notificationService.MarkAsRead(id);

        RunOnUI(() =>
        {
            lock (_stateLock)
            {
                var item = NotificationHistory.FirstOrDefault(n => n.Id == id);
                if (item != null)
                {
                    item.IsRead = true;
                    UpdateUnreadCount();
                }
            }
        });

        _logger.LogDebug("Marked notification {NotificationId} as read", id);
    }

    /// <summary>
    /// Updates the unread count based on current history.
    /// </summary>
    private void UpdateUnreadCount()
    {
        lock (_stateLock)
        {
            var items = NotificationHistory.ToList();
            UnreadCount = items.Count(n => !n.IsRead);
            BadgeCount = items.Count(n => !n.IsRead && n.ShowInBadge);
        }
    }

    /// <summary>
    /// Handles notification added from service.
    /// </summary>
    /// <param name="message">The notification message.</param>
    private void OnNotificationAdded(NotificationMessage message)
    {
        if (_disposed)
        {
            return;
        }

        AddNotification(message);
    }
}
