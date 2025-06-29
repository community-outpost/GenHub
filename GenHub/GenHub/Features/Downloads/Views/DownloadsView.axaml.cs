using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// View for the Downloads feature.
/// </summary>
public partial class DownloadsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsView"/> class.
    /// </summary>
    public DownloadsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads and initializes the XAML components for this view.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
