using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Interaction logic for PublisherCardView.xaml.
/// </summary>
public partial class PublisherCardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherCardView"/> class.
    /// </summary>
    public PublisherCardView()
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
