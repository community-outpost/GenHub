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

    private void OnUnmuteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.UnmuteCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }

    private void OnMuteSessionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.MuteSessionCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }

    private void OnMutePersistentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationFeedViewModel vm)
        {
            vm.MutePersistentCommand.Execute(null);
            CloseOptionsFlyout();
        }
    }
}
