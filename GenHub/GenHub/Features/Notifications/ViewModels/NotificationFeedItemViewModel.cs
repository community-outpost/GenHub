using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// ViewModel for a single item in the notification feed.
/// </summary>
public partial class NotificationFeedItemViewModel : ViewModelBase, IDisposable
{
    private readonly NotificationMessage _message;
    private readonly Action<Guid> _onMarkAsRead;
    private readonly Action<Guid> _onDismiss;
    private readonly ILogger<NotificationFeedItemViewModel> _logger;
    [ObservableProperty]
    private bool _isRead;

    /// <summary>
    /// Gets the unique identifier for this notification.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the notification type.
    /// </summary>
    public NotificationType Type { get; }

    /// <summary>
    /// Gets the notification title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the notification message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the timestamp when the notification was created.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the formatted time string for display.
    /// </summary>
    public string FormattedTime => FormatTimestamp(Timestamp);

    /// <summary>
    /// Gets a value indicating whether this notification should be shown in the badge count.
    /// </summary>
    public bool ShowInBadge { get; }

    /// <summary>
    /// Gets the collection of actions available for this notification.
    /// </summary>
    public ObservableCollection<NotificationActionViewModel> Actions { get; }

    /// <summary>
    /// Gets the icon path data based on the notification type.
    /// </summary>
    public string IconPath => Type switch
    {
        NotificationType.Info => NotificationConstants.InfoIconPath,
        NotificationType.Success => NotificationConstants.SuccessIconPath,
        NotificationType.Warning => NotificationConstants.WarningIconPath,
        NotificationType.Error => NotificationConstants.ErrorIconPath,
        _ => string.Empty,
    };

    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse(NotificationConstants.InfoColor));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse(NotificationConstants.SuccessColor));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse(NotificationConstants.WarningColor));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse(NotificationConstants.ErrorColor));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Colors.Gray);

    /// <summary>
    /// Gets the background brush for the notification based on its type.
    /// </summary>
    public IBrush BackgroundBrush => Type switch
    {
        NotificationType.Info => InfoBrush,
        NotificationType.Success => SuccessBrush,
        NotificationType.Warning => WarningBrush,
        NotificationType.Error => ErrorBrush,
        _ => DefaultBrush,
    };

    /// <summary>
    /// Gets the command to dismiss this notification.
    /// </summary>
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationFeedItemViewModel"/> class.
    /// </summary>
    /// <param name="message">The notification message.</param>
    /// <param name="onMarkAsRead">Callback to invoke when the notification is marked as read.</param>
    /// <param name="onDismiss">Callback to invoke when the notification is dismissed.</param>
    /// <param name="logger">The logger instance.</param>
    public NotificationFeedItemViewModel(
        NotificationMessage message,
        Action<Guid> onMarkAsRead,
        Action<Guid> onDismiss,
        ILogger<NotificationFeedItemViewModel> logger)
    {
        _message = message ?? throw new ArgumentNullException(nameof(message));
        _onMarkAsRead = onMarkAsRead ?? throw new ArgumentNullException(nameof(onMarkAsRead));
        _onDismiss = onDismiss ?? throw new ArgumentNullException(nameof(onDismiss));
        _logger = logger;

        Id = message.Id;
        Type = message.Type;
        Title = message.Title;
        Message = message.Message;
        Timestamp = message.Timestamp;
        ShowInBadge = message.ShowInBadge;
        _isRead = message.IsRead;

        // Create action view models
        Actions = new ObservableCollection<NotificationActionViewModel>(
            message.Actions?.Select(a => new NotificationActionViewModel(a, () => ExecuteAction(a))) ?? Enumerable.Empty<NotificationActionViewModel>());

        DismissCommand = new RelayCommand(ExecuteDismiss);
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Formats a timestamp for display.
    /// </summary>
    /// <param name="timestamp">The timestamp to format.</param>
    /// <returns>The formatted time string.</returns>
    private static string FormatTimestamp(DateTime timestamp)
    {
        var now = DateTime.Now;
        var localTimestamp = timestamp.ToLocalTime();
        var diff = now - localTimestamp;

        if (diff.TotalMinutes < 1)
        {
            return "Just now";
        }
        else if (diff.TotalMinutes < 60)
        {
            return $"{diff.Minutes}m ago";
        }
        else if (diff.TotalHours < 24)
        {
            return $"{diff.Hours}h ago";
        }
        else if (diff.TotalDays < 7)
        {
            return $"{diff.Days}d ago";
        }
        else
        {
            return localTimestamp.ToString("MMM dd");
        }
    }

    /// <summary>
    /// Executes a notification action.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    private void ExecuteAction(NotificationAction action)
    {
        _logger.LogDebug("Executing action '{ActionText}' for notification {NotificationId}", action.Text, Id);
        action.Callback?.Invoke();

        if (action.DismissOnExecute)
        {
            ExecuteDismiss();
        }
    }

    /// <summary>
    /// Dismisses this notification.
    /// </summary>
    private void ExecuteDismiss()
    {
        _logger.LogDebug("Dismissing notification {NotificationId}", Id);
        _onDismiss?.Invoke(Id);
    }
}
