using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using System;

namespace GenHub.Features.Tools.ReplayManager.Views;

/// <summary>
/// Dialog window for selecting an available game client to create a dedicated profile for a replay.
/// </summary>
public partial class GameClientSelectionView : GenHubWindow
{
    private GameClientSelectionViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientSelectionView"/> class.
    /// </summary>
    public GameClientSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientSelectionView"/> class with a specific view model.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public GameClientSelectionView(GameClientSelectionViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
        }

        if (DataContext is GameClientSelectionViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestClose += OnRequestClose;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

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
