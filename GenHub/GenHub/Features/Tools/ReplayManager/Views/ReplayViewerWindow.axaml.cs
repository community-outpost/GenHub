using System;
using Avalonia.Controls;
using GenHub.Features.Tools.ReplayManager.ViewModels;

namespace GenHub.Features.Tools.ReplayManager.Views;

/// <summary>
/// Window for viewing parsed replay metadata.
/// </summary>
public partial class ReplayViewerWindow : Window
{
    private ReplayViewerViewModel? _currentViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayViewerWindow"/> class.
    /// </summary>
    public ReplayViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested -= OnCloseRequested;
        }

        _currentViewModel = DataContext as ReplayViewerViewModel;
        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
