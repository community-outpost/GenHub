using Avalonia.Controls;
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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
