using Avalonia.Controls;
using GenHub.Features.Downloads.ViewModels;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// View for the dependency preview dialog.
/// </summary>
public partial class DependencyPreviewView : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPreviewView"/> class.
    /// </summary>
    public DependencyPreviewView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPreviewView"/> class with a view model.
    /// </summary>
    /// <param name="viewModel">The view model for this view.</param>
    public DependencyPreviewView(DependencyPreviewViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => Close();
    }
}
