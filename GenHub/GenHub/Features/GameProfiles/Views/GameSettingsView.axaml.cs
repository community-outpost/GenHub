using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game settings (Options.ini) management.
/// </summary>
public partial class GameSettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsView"/> class.
    /// </summary>
    public GameSettingsView()
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