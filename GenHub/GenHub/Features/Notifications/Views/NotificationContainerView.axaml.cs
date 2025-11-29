using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Notifications.Views;

/// <summary>
/// Container view for displaying all active notification toasts.
/// </summary>
public partial class NotificationContainerView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationContainerView"/> class.
    /// </summary>
    public NotificationContainerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}