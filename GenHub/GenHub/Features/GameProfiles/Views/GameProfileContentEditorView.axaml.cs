using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for editing game profile content.
/// </summary>
public partial class GameProfileContentEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileContentEditorView"/> class.
    /// </summary>
    public GameProfileContentEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
