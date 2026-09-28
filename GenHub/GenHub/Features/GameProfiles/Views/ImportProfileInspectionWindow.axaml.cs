using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for inspecting and importing shared game profiles.
/// </summary>
public partial class ImportProfileInspectionWindow : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProfileInspectionWindow"/> class.
    /// </summary>
    public ImportProfileInspectionWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override bool DisposeDataContextOnClose => true;

    /// <inheritdoc/>
    protected override bool TitleBarDoubleClickMaximizes => false;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
