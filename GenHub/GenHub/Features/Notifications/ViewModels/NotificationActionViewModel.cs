using System;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// ViewModel for a single notification action button.
/// </summary>
public partial class NotificationActionViewModel : ObservableObject
{
    private readonly NotificationAction _action;

    /// <summary>
    /// Gets the action style.
    /// </summary>
    public NotificationActionStyle Style => _action.Style;

    /// <summary>
    /// Gets the text to display on the action button.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the command to execute when the action button is clicked.
    /// </summary>
    public ICommand ExecuteCommand { get; }

    /// <summary>
    /// Gets the background brush for the action button based on its style.
    /// </summary>
    public IBrush BackgroundBrush => Style switch
    {
        NotificationActionStyle.Primary => new SolidColorBrush(Color.Parse("#4A9EFF")),
        NotificationActionStyle.Secondary => new SolidColorBrush(Color.Parse("#6B7280")),
        NotificationActionStyle.Danger => new SolidColorBrush(Color.Parse("#EF4444")),
        NotificationActionStyle.Success => new SolidColorBrush(Color.Parse("#10B981")),
        _ => new SolidColorBrush(Colors.Gray),
    };

    /// <summary>
    /// Gets the foreground brush for the action button based on its style.
    /// </summary>
    public IBrush ForegroundBrush => Style switch
    {
        NotificationActionStyle.Primary => new SolidColorBrush(Colors.White),
        NotificationActionStyle.Secondary => new SolidColorBrush(Colors.White),
        NotificationActionStyle.Danger => new SolidColorBrush(Colors.White),
        NotificationActionStyle.Success => new SolidColorBrush(Colors.White),
        _ => new SolidColorBrush(Colors.White),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationActionViewModel"/> class.
    /// </summary>
    /// <param name="action">The notification action.</param>
    /// <param name="onExecute">Callback to invoke when the action is executed.</param>
    public NotificationActionViewModel(
        NotificationAction action,
        Action? onExecute)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        Text = action.Text;
        ExecuteCommand = new RelayCommand(() => onExecute?.Invoke());
    }
}
