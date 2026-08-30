using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using GenHub.Features.Downloads.ViewModels;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// dialog window for selecting a profile to add content to.
/// displays compatible profiles first, followed by incompatible profiles with warnings.
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
    /// <param name="viewModel">The profile selection view model.</param>
    public ProfileSelectionView(ProfileSelectionViewModel viewModel)
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
