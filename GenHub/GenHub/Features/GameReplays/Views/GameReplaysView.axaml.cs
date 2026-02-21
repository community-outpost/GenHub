using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameReplays.Views;

/// <summary>
/// Code-behind for GameReplays view.
/// </summary>
public partial class GameReplaysView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameReplaysView"/> class.
    /// </summary>
    public GameReplaysView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
