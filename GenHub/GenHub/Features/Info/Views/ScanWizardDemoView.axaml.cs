using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for ScanWizardDemoView.axaml.
/// </summary>
public partial class ScanWizardDemoView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScanWizardDemoView"/> class.
    /// </summary>
    public ScanWizardDemoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
