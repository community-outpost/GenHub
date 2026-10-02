using Avalonia.Controls;

namespace GenHub.Features.Tools.RmlEditor.Views;

/// <summary>
/// View for the RML editor tool.
/// </summary>
public partial class RmlEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RmlEditorView"/> class.
    /// </summary>
    public RmlEditorView()
    {
        InitializeComponent();
        Focusable = true;
    }
}
