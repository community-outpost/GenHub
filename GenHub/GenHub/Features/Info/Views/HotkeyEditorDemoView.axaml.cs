using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for HotkeyEditorDemoView.axaml.
/// </summary>
public partial class HotkeyEditorDemoView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyEditorDemoView"/> class.
    /// </summary>
    public HotkeyEditorDemoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
