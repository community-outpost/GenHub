using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Notifications.Views;

/// <summary>
/// View for displaying a single notification toast.
/// </summary>
public partial class NotificationToastView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationToastView"/> class.
    /// </summary>
    public NotificationToastView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}