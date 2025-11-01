using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Interaction logic for <c>GameProfileSettingsWindow.axaml</c>.
/// </summary>
public partial class GameProfileSettingsWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsWindow"/> class.
    /// </summary>
    public GameProfileSettingsWindow()
    {
        InitializeComponent();

        // Subscribe to DataContext changes to handle commands
        DataContextChanged += OnDataContextChanged;
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
}
