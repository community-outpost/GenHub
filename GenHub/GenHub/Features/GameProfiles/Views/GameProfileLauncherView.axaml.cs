using Avalonia.Controls;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for the Game Profiles feature.
/// </summary>
public partial class GameProfileLauncherView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherView"/> class.
    /// </summary>
    public GameProfileLauncherView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
