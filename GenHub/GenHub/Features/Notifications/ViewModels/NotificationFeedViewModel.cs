using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    [NotifyPropertyChangedFor(nameof(NotificationCountDisplay))]
    private int _badgeCount;

    /// <summary>
    /// Gets a value indicating whether there are unread notifications that should be shown in the badge.
    /// </summary>
    public bool HasUnreadNotifications => BadgeCount > 0;

    /// <summary>
    /// Gets the text to show in the notification badge (number or "99+").
    /// </summary>
    public string NotificationCountDisplay => BadgeCount > 99 ? "99+" : BadgeCount.ToString();

    /// <summary>
    /// Gets the collection of notification history items.
    /// </summary>
    public ObservableCollection<NotificationFeedItemViewModel> NotificationHistory { get; }

    /// <summary>
    /// Gets a value indicating whether there are any notifications.
    /// </summary>
    public bool HasNotifications => NotificationHistory?.Any() == true;

    /// <summary>
    /// Gets the current notification mute state from the service.
    /// </summary>
    public NotificationMuteState MuteState => _notificationService.MuteState;

    /// <summary>
    /// Gets or sets whether to show the strike (diagonal line) over the bell icon (true when muted).
    /// Stored so UI bindings update reliably when mute state changes.
    /// </summary>
    [ObservableProperty]
    private bool _showMuteStrike;

    /// <summary>
    /// Raised when mute state changes so the title bar bell can update (avoids relying on messenger).
    /// </summary>
    public event Action? MuteStrikeChanged;

    /// <summary>
    /// Raised when badge count changes so the title bar red circle can update.
    /// </summary>
    public event Action? BadgeCountChanged;

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
        _showMuteStrike = notificationService.MuteState != NotificationMuteState.None;

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

                    // Count notification for badge if explicitly allowed, or if muted while feed is closed
                    // so unseen notifications are reflected in the badge indicator.
                    if (message.ShowInBadge || (MuteState != NotificationMuteState.None && !IsFeedOpen))
                    {
                        BadgeCount++;
                    }
                }

                OnPropertyChanged(nameof(HasNotifications));
                BadgeCountChanged?.Invoke();
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

        // Reset badge count when opening the feed (hides red circle)
        if (IsFeedOpen)
        {
            BadgeCount = 0;
            BadgeCountChanged?.Invoke();
            _logger.LogInformation("Feed opened, badge count reset to 0");
        }
        else
        {
            _logger.LogInformation("Feed closed");
        }

        _logger.LogInformation("Feed toggled: {IsOpen}", IsFeedOpen);
    }

    /// <summary>
    /// Turns notifications on (unmute).
    /// </summary>
    [RelayCommand]
    private async Task Unmute(CancellationToken cancellationToken = default)
    {
        await _notificationService.Unmute(cancellationToken);
        NotifyMuteStateChanged();
        _logger.LogInformation("Notifications turned on");
    }

    /// <summary>
    /// Mutes notifications for this session only.
    /// </summary>
    [RelayCommand]
    private void MuteSession()
    {
        _notificationService.MuteSession();
        NotifyMuteStateChanged();
        _logger.LogInformation("Notifications muted for session");
    }

    /// <summary>
    /// Mutes notifications persistently (until user turns on again).
    /// </summary>
    [RelayCommand]
    private async Task MutePersistent(CancellationToken cancellationToken = default)
    {
        await _notificationService.MutePersistent(cancellationToken);
        NotifyMuteStateChanged();
        _logger.LogInformation("Notifications muted always");
    }

    /// <summary>
    /// Updates mute-related properties and notifies listeners when the notification mute state changes.
    /// </summary>
    /// <remarks>
    /// Refreshes binding properties and raises <see cref="MuteStrikeChanged"/> so UI elements,
    /// such as the notification bell, update immediately. Must be called on the UI thread.
    /// </remarks>
    private void NotifyMuteStateChanged()
    {
        // Update stored strike state so bell icon binding updates (already on UI thread from menu click)
        ShowMuteStrike = _notificationService.MuteState != NotificationMuteState.None;
        OnPropertyChanged(nameof(MuteState));

        // Direct callback so title bar bell updates (does not rely on messenger)
        MuteStrikeChanged?.Invoke();
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
                BadgeCount = 0;
                OnPropertyChanged(nameof(HasNotifications));
                OnPropertyChanged(nameof(HasUnreadNotifications));
                OnPropertyChanged(nameof(NotificationCountDisplay));
                BadgeCountChanged?.Invoke();
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
            BadgeCount = items.Count(n => !n.IsRead);
            BadgeCountChanged?.Invoke();
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
