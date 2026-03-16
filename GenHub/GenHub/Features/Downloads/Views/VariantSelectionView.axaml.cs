using Avalonia.Controls;
using GenHub.Features.Downloads.ViewModels;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// View for selecting a variant when content has multiple game type variants.
/// </summary>
public partial class VariantSelectionView : Window
{
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
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => Close();
    }
}
