using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for WndEditorDemoView.axaml.
/// </summary>
public partial class WndEditorDemoView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorDemoView"/> class.
    /// </summary>
    public WndEditorDemoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
