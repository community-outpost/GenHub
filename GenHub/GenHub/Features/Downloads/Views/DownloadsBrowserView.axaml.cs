using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for DownloadsBrowserView.
/// </summary>
public partial class DownloadsBrowserView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsBrowserView"/> class and loads its associated XAML components.
    /// </summary>
    public DownloadsBrowserView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}