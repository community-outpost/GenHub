using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Downloads.ViewModels;
using System;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// view for the dependency preview dialog.
/// </summary>
public partial class DependencyPreviewView : GenHubWindow
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
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
