using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Shared content view for game profile settings.
/// Used by both the Window and Demo implementations to maintain a single source of truth.
/// </summary>
public partial class GameProfileSettingsContentView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsContentView"/> class.
    /// </summary>
    public GameProfileSettingsContentView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
