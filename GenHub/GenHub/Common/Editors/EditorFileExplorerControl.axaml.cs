using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Common.Editors;

/// <summary>
/// Code-behind for the shared editor file explorer control.
/// </summary>
public partial class EditorFileExplorerControl : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EditorFileExplorerControl"/> class.
    /// </summary>
    public EditorFileExplorerControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
