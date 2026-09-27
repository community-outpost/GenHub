using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Downloads.ViewModels;
using System;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// dialog window for selecting a profile to add content to.
/// displays compatible profiles first, followed by incompatible profiles with warnings.
/// </summary>
public partial class ProfileSelectionView : GenHubWindow
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
    /// <param name="viewModel">The profile selection view model.</param>
    public ProfileSelectionView(ProfileSelectionViewModel viewModel)
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

        // cleanup event subscription
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }

    /// <inheritdoc/>
    protected override void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.CancelCommand.Execute(null);
        }
        else
        {
            Close();
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
