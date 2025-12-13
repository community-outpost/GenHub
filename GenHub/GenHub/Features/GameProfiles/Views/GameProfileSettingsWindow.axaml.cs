using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsWindow"/> class.
    /// </summary>
    public GameProfileSettingsWindow()
    {
        InitializeComponent();

        // Subscribe to DataContext changes to handle commands
        DataContextChanged += OnDataContextChanged;

        // Restore saved window size
        RestoreWindowSize();

        // Subscribe to property changes to save window size
        PropertyChanged += OnPropertyChanged;
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
        if (Width > 0 && Height > 0)
        {
            _savedWidth = Width;
            _savedHeight = Height;
        }
    }
}
