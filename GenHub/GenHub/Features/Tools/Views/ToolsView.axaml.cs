using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Tools.Views;

/// <summary>
/// View for managing and displaying tool plugins.
/// </summary>
public partial class ToolsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsView"/> class.
    /// </summary>
    public ToolsView()
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
