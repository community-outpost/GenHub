using Avalonia.Controls;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding a new artifact.
/// File browsing is handled by BrowseLocalFileCommand in the ViewModel.
/// </summary>
public partial class AddArtifactDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddArtifactDialogView"/> class.
    /// </summary>
    public AddArtifactDialogView()
    {
        InitializeComponent();
    }
}
