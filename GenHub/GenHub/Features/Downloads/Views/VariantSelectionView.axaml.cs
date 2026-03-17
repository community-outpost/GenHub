using System;
using Avalonia.Controls;
using GenHub.Features.Downloads.ViewModels;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// View for selecting a variant when content has multiple game type variants.
/// </summary>
public partial class VariantSelectionView : Window
{
    private VariantSelectionViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSelectionView"/> class.
    /// </summary>
    public VariantSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSelectionView"/> class with a view model.
    /// </summary>
    /// <param name="viewModel">The view model for this view.</param>
    public VariantSelectionView(VariantSelectionViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from previous view model
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
        }

        // Wire up close functionality to the ViewModel
        if (DataContext is VariantSelectionViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestClose += OnRequestClose;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Cleanup event subscription
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
}
