using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for ContentDetailView.
/// </summary>
public partial class ContentDetailView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDetailView"/> class and loads its XAML-defined components.
    /// </summary>
    public ContentDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}