using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for ModBuilderDemoView.axaml.
/// </summary>
public partial class ModBuilderDemoView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderDemoView"/> class.
    /// </summary>
    public ModBuilderDemoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
