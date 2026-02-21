using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Features.Notifications.ViewModels;

namespace GenHub.Features.Notifications.Views;

/// <summary>
/// Code-behind for NotificationFeedView.
/// </summary>
public partial class NotificationFeedView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationFeedView"/> class.
    /// </summary>
    public NotificationFeedView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CloseOptionsFlyout()
    {
        if (this.FindControl<Button>("OptionsButton")?.Flyout is Flyout flyout)
            flyout.Hide();
    }

    /// <summary>
    /// Handles the Unmute action and executes the corresponding view-model command.
    /// </summary>
    private void OnUnmuteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.UnmuteCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }

    /// <summary>
    /// Handles the Mute for Session action and executes the corresponding view-model command.
    /// </summary>
    private void OnMuteSessionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.MuteSessionCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }

    /// <summary>
    /// Handles the Persistent Mute action and executes the corresponding view-model command.
    /// </summary>
    private void OnMutePersistentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.MutePersistentCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }
}
