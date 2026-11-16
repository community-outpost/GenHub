using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Notifications.Views;

/// <summary>
/// Code-behind for NotificationFeedItemView.
/// </summary>
public partial class NotificationFeedItemView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationFeedItemView"/> class.
    /// </summary>
    public NotificationFeedItemView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
