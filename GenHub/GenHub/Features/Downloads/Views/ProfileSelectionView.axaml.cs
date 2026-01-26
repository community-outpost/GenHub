using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.Downloads.ViewModels;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Dialog window for selecting a profile to add content to.
/// Displays compatible profiles first, followed by incompatible profiles with warnings.
/// </summary>
public partial class ProfileSelectionView : Window
{
    private ProfileSelectionViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSelectionView"/> class.
    /// </summary>
    public ProfileSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSelectionView"/> class with a specific view model.
    /// </summary>
    /// <param name="viewModel">The profile selection view model to use as the view's DataContext.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is null.</exception>
    public ProfileSelectionView(ProfileSelectionViewModel viewModel)
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
        if (DataContext is ProfileSelectionViewModel viewModel)
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

    /// <summary>
    /// Closes the window in response to the view model's RequestClose event.
    /// </summary>
    /// <param name="sender">The event source, typically the requesting view model.</param>
    /// <param name="e">Event arguments (unused).</param>
    private void OnRequestClose(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Loads and applies the Avalonia XAML associated with this view, initializing its UI components.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}