using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;

namespace GenHub.Features.GameProfiles.Views.Wizard;

/// <summary>
/// Interaction logic for the Setup Wizard dialog.
/// </summary>
public partial class SetupWizardView : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetupWizardView"/> class.
    /// </summary>
    public SetupWizardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
