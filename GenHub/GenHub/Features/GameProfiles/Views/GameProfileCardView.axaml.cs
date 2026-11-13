using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Card view for a game profile.
/// </summary>
public partial class GameProfileCardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileCardView"/> class.
    /// </summary>
    public GameProfileCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the XAML components.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
