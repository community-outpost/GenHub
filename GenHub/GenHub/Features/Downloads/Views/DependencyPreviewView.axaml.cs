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
    private DependencyPreviewViewModel? _viewModel;

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

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // unsubscribe from previous view model
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
        }

        // wire up close functionality to the view model
        if (DataContext is DependencyPreviewViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestClose += OnRequestClose;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // cleanup event subscription so a cached/shared view model cannot leak the view
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
            _viewModel = null;
        }
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
