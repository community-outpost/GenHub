using System;
using System.Threading;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// ViewModel for a single notification toast item.
/// </summary>
public partial class NotificationItemViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<NotificationItemViewModel> _logger;
    private readonly Action<Guid> _onDismissCallback;
    private Timer? _autoDismissTimer;

    [ObservableProperty]
    private bool _isVisible;

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
    /// Gets a value indicating whether this notification has an action button.
    /// </summary>
    public bool IsActionable { get; }

    /// <summary>
    /// Gets the action button text.
    /// </summary>
    public string? ActionText { get; }

    /// <summary>
    /// Gets the action to execute when the action button is clicked.
    /// </summary>
    public Action? Action { get; }

    /// <summary>
    /// Gets the icon path data based on notification type.
    /// </summary>
    public string IconPath => Type switch
    {
        NotificationType.Info => NotificationConstants.InfoIconPath,
        NotificationType.Success => NotificationConstants.SuccessIconPath,
        NotificationType.Warning => NotificationConstants.WarningIconPath,
        NotificationType.Error => NotificationConstants.ErrorIconPath,
        _ => string.Empty
    };

    /// <summary>
    /// Gets the background brush based on notification type.
    /// </summary>
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse(NotificationConstants.InfoColor));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse(NotificationConstants.SuccessColor));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse(NotificationConstants.WarningColor));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse(NotificationConstants.ErrorColor));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Colors.Gray);

    public IBrush BackgroundBrush => Type switch
    {
        NotificationType.Info => InfoBrush,
        NotificationType.Success => SuccessBrush,
        NotificationType.Warning => WarningBrush,
        NotificationType.Error => ErrorBrush,
        _ => DefaultBrush
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationItemViewModel"/> class.
    /// </summary>
    /// <param name="notification">The notification message.</param>
    /// <param name="onDismissCallback">Callback to invoke when notification is dismissed.</param>
    /// <param name="logger">The logger instance.</param>
    public NotificationItemViewModel(
        NotificationMessage notification,
        Action<Guid> onDismissCallback,
        ILogger<NotificationItemViewModel> logger)
    {
        _logger = logger;
        _onDismissCallback = onDismissCallback;

        Id = notification.Id;
        Type = notification.Type;
        Title = notification.Title;
        Message = notification.Message;
        Timestamp = notification.Timestamp;
        IsActionable = notification.IsActionable;
        ActionText = notification.ActionText;
        Action = notification.Action;
        _isVisible = false;

        DismissCommand = new RelayCommand(ExecuteDismiss);
        ActionCommand = new RelayCommand(ExecuteAction, () => IsActionable);

        if (notification.AutoDismissMilliseconds.HasValue)
        {
            StartDismissTimer(notification.AutoDismissMilliseconds.Value);
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsVisible = true;
        });
    }

    /// <summary>
    /// Gets the command to dismiss the notification.
    /// </summary>
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Gets the command to execute the notification action.
    /// </summary>
    public ICommand ActionCommand { get; }

    /// <summary>
    /// Starts the auto-dismiss timer.
    /// </summary>
    /// <param name="milliseconds">The timeout in milliseconds.</param>
    public void StartDismissTimer(int milliseconds)
    {
        _autoDismissTimer?.Dispose();
        _autoDismissTimer = new Timer(
            _ => Dispatcher.UIThread.Post(ExecuteDismiss),
            null,
            milliseconds,
            Timeout.Infinite);
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        _autoDismissTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ExecuteDismiss()
    {
        _logger.LogDebug("Dismissing notification {NotificationId}", Id);
        IsVisible = false;

        // Call the dismiss callback directly - we're already on the UI thread
        _onDismissCallback?.Invoke(Id);
    }

    private void ExecuteAction()
    {
        if (IsActionable && Action != null)
        {
            _logger.LogDebug("Executing action for notification {NotificationId}", Id);
            Action.Invoke();
            ExecuteDismiss();
        }
    }
}
