using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for ContentCardView.
/// </summary>
public partial class ContentCardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentCardView"/> class and loads its XAML-defined components.
    /// </summary>
    public ContentCardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}