using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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
        var optionsButton = this.FindControl<Button>("OptionsButton");
        if (optionsButton?.Flyout is Flyout flyout)
        {
            flyout.Opened += (_, _) =>
            {
                if (flyout.Content is Control content)
                    content.DataContext = DataContext;
            };
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Closes the options flyout after a mute option is chosen (Command binding handles the action).
    /// </summary>
    private void CloseOptionsFlyout(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Button>("OptionsButton")?.Flyout is Flyout f)
            f.Hide();
    }
}