using GenHub.Common.Controls;
using GenHub.Features.Tools.ModBuilder.ViewModels;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Dialog window for the ModBuilder Game Asset and File Manager.
/// </summary>
public partial class FileManagerDialog : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerDialog"/> class.
    /// </summary>
    public FileManagerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerDialog"/> class with the specified ViewModel.
    /// </summary>
    /// <param name="viewModel">The file manager view model.</param>
    public FileManagerDialog(FileManagerViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}

