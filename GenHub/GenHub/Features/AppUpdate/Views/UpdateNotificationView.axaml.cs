using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.AppUpdate.Views;

/// <summary>
/// Interaction logic for <see cref="UpdateNotificationView"/>.
/// </summary>
public partial class UpdateNotificationView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateNotificationView"/> class.
    /// </summary>
    public UpdateNotificationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}