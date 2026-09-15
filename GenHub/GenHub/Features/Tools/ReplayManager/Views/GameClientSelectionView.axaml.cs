using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using GenHub.Features.Tools.ReplayManager.ViewModels;

namespace GenHub.Features.Tools.ReplayManager.Views;

/// <summary>
/// Dialog window for selecting an available game client to create a dedicated profile for a replay.
/// </summary>
public partial class GameClientSelectionView : Window
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
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            var maximizeIcon = (change.Sender as Control)?.FindControl<Path>("MaximizeIcon");
            if (maximizeIcon != null)
            {
                maximizeIcon.Data = WindowState == WindowState.Maximized
                    ? Geometry.Parse("M4,8H8V4H20V16H16V20H4V8M16,8V6H10V8H16M6,10V18H14V10H6Z")
                    : Geometry.Parse("M4,4H20V20H4V4M6,8V18H18V8H6Z");
            }
        }
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

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, new RoutedEventArgs());
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
