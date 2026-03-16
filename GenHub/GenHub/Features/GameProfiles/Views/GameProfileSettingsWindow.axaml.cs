using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Interaction logic for <c>GameProfileSettingsWindow.axaml</c>.
/// </summary>
public partial class GameProfileSettingsWindow : Window
{
    // Static fields to persist window size across instances
    private static double? _savedWidth;
    private static double? _savedHeight;

    // Fields for manual drag detection to allow double-click to work
    private bool _isMouseDown;
    private Point _mouseDownPosition;
    private PointerPressedEventArgs? _pressedEventArgs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsWindow"/> class.
    /// </summary>
    public GameProfileSettingsWindow()
    {
        InitializeComponent();

        // Wire up drag handlers to the header in the shared content view
        WireUpDragHandlers();

        // Subscribe to DataContext changes to handle commands
        DataContextChanged += OnDataContextChanged;

        // Restore saved window size
        RestoreWindowSize();

        // Subscribe to property changes to save window size
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Handles pointer pressed on the header to enable window dragging and maximizing.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            _isMouseDown = false;
            _pressedEventArgs = null;
        }
        else
        {
            _isMouseDown = true;
            _mouseDownPosition = e.GetPosition(this);
            _pressedEventArgs = e;
        }
    }

    /// <summary>
    /// Handles pointer moved to initiate drag only after a threshold, allowing double-clicks to pass through.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMouseDown || _pressedEventArgs == null)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        var distance = Math.Sqrt(Math.Pow(currentPosition.X - _mouseDownPosition.X, 2) + Math.Pow(currentPosition.Y - _mouseDownPosition.Y, 2));

        // Drag threshold of 3 pixels
        if (distance > 3)
        {
            if (WindowState == WindowState.Maximized)
            {
                var screenX = Position.X + (currentPosition.X * RenderScaling);
                var screenY = Position.Y + (currentPosition.Y * RenderScaling);

                WindowState = WindowState.Normal;

                var targetWidth = _savedWidth ?? Width;
                var newX = screenX - ((targetWidth * RenderScaling) / 2);
                var newY = screenY - (_mouseDownPosition.Y * RenderScaling);

                Position = new PixelPoint((int)newX, (int)newY);
            }

            BeginMoveDrag(_pressedEventArgs);
            _isMouseDown = false;
            _pressedEventArgs = null;
        }
    }

    /// <summary>
    /// Handles pointer released to reset drag state.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isMouseDown = false;
        _pressedEventArgs = null;
    }

    /// <summary>
    /// Handles the toggle fullscreen button click.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnToggleFullscreenClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Override to unsubscribe from events when window is closed.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        // Save window size before closing
        SaveWindowSize();

        base.OnClosed(e);
    }

    /// <summary>
    /// Wires up pointer event handlers to the header border in the shared content view.
    /// </summary>
    private void WireUpDragHandlers()
    {
        // Find the named header border in the shared content view
        if (this.FindControl<GameProfileSettingsContentView>("ContentView")?.FindControl<Border>("HeaderBorder") is { } headerBorder)
        {
            headerBorder.PointerPressed += OnHeaderPointerPressed;
            headerBorder.PointerMoved += OnHeaderPointerMoved;
            headerBorder.PointerReleased += OnHeaderPointerReleased;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Handles DataContext changes to wire up commands.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            // Subscribe to the close request from the view model
            viewModel.CloseRequested += OnCloseRequested;

            // Note: GameSettings will be initialized in InitializeForProfileAsync if editing,
            // or via InitializeForNewProfileAsync if creating new.
            // No need to call InitializeAsync here as it would load default settings.
        }
    }

    /// <summary>
    /// Handles the close request from the view model.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Handles property changes to save window size when it changes.
    /// </summary>
    private void OnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty || e.Property == HeightProperty)
        {
            SaveWindowSize();
        }
    }

    /// <summary>
    /// Restores the window size from saved static fields.
    /// </summary>
    private void RestoreWindowSize()
    {
        Width = _savedWidth ?? UiConstants.DefaultProfileSettingsWidth;
        Height = _savedHeight ?? UiConstants.DefaultProfileSettingsHeight;
    }

    /// <summary>
    /// Saves the current window size to static fields.
    /// </summary>
    private void SaveWindowSize()
    {
        // Only save size if window is in normal state (not maximized or minimized)
        if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
        {
            _savedWidth = Width;
            _savedHeight = Height;
        }
    }
}
