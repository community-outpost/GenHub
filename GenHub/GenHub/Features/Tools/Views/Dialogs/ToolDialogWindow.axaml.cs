using Avalonia.Controls;
using GenHub.Common.Controls;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// Generic window for hosting tool dialogs.
/// </summary>
public partial class ToolDialogWindow : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDialogWindow"/> class.
    /// </summary>
    public ToolDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the dialog content inside the MainContent placeholder.
    /// </summary>
    /// <param name="content">The content control to display.</param>
    public void SetDialogContent(Control content)
    {
        var mainContent = this.FindControl<ContentControl>("MainContent");
        if (mainContent != null)
        {
            mainContent.Content = content;
        }
        else
        {
            Content = content;
        }
    }
}
