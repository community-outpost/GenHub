using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.Helpers;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Interaction logic for <c>GameProfileSettingsWindow.axaml</c>.
/// </summary>
public partial class GameProfileSettingsWindow : Window
{
    // Static fields to persist window size across instances
    private static double? _savedWidth;
    private static double? _savedHeight;
    private static WindowState? _savedWindowState;
    private bool _isClosing;

    /// <summary>
    /// Event raised when the persisted sidebar width is changed by the user.
    /// </summary>
    public static event EventHandler<double>? SidebarWidthChanged;

    /// <summary>
    /// Gets or sets the persisted width of the profile settings sidebar.
    /// </summary>
    public static double SavedSidebarWidth { get; set; } = UiConstants.DefaultProfileSettingsSidebarWidth;

    /// <summary>
    /// Updates the persisted sidebar width and notifies all open views.
    /// </summary>
    /// <param name="width">The new sidebar width.</param>
    public static void UpdateSidebarWidth(double width)
    {
        if (width <= 0) return;
        SavedSidebarWidth = width;

        try
        {
            var userSettingsService = App.Services?.GetService<IUserSettingsService>();
            userSettingsService?.Update(s => s.ProfileSettingsSidebarWidth = width);
            _ = userSettingsService?.SaveAsync();
        }
        catch
        {
            // Ignore settings save errors in design-time or unit-test environments
        }

        SidebarWidthChanged?.Invoke(null, width);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsWindow"/> class.
    /// </summary>
    public GameProfileSettingsWindow()
    {
        InitializeComponent();
        WindowChromeHelper.ApplyPlatformDecorations(this);

        // Subscribe to DataContext changes to handle commands
        DataContextChanged += OnDataContextChanged;

        // Restore saved window size
        RestoreWindowSize();

        // Subscribe to property changes to save window size
        PropertyChanged += OnPropertyChanged;

        // Refresh hotswap state when window is focused/activated
        Activated += OnWindowActivated;
    }

    /// <summary>
    /// Handles pointer pressed on the header to enable window dragging and maximizing.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
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

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_savedWindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SaveWindowSize();
        _isClosing = true;
        base.OnClosing(e);
    }

    /// <summary>
    /// Override to unsubscribe from events when window is closed.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnClosed(EventArgs e)
    {
        Activated -= OnWindowActivated;

        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        // Save window size before closing
        SaveWindowSize();

        base.OnClosed(e);
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            await viewModel.RefreshHotswapStateAsync();
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
        if (_isClosing)
        {
            return;
        }

        if (e.Property == WidthProperty || e.Property == HeightProperty || e.Property == WindowStateProperty)
        {
            SaveWindowSize();
        }
    }

    /// <summary>
    /// Restores the window size and state from saved static fields or user settings.
    /// </summary>
    private void RestoreWindowSize()
    {
        try
        {
            var userSettingsService = App.Services?.GetService<IUserSettingsService>();
            var settings = userSettingsService?.Get();
            if (settings != null)
            {
                _savedWidth ??= settings.ProfileSettingsWindowWidth;
                _savedHeight ??= settings.ProfileSettingsWindowHeight;
                if (!_savedWindowState.HasValue && settings.ProfileSettingsWindowIsMaximized.HasValue)
                {
                    _savedWindowState = settings.ProfileSettingsWindowIsMaximized.Value ? WindowState.Maximized : WindowState.Normal;
                }

                if (settings.ProfileSettingsSidebarWidth.HasValue)
                {
                    SavedSidebarWidth = settings.ProfileSettingsSidebarWidth.Value;
                }
            }
        }
        catch
        {
            // Fallback to defaults if settings service is unavailable
        }

        Width = _savedWidth ?? UiConstants.DefaultProfileSettingsWidth;
        Height = _savedHeight ?? UiConstants.DefaultProfileSettingsHeight;
        if (_savedWindowState.HasValue)
        {
            WindowState = _savedWindowState.Value;
        }
    }

    /// <summary>
    /// Saves the current window size and state to static fields and user settings.
    /// </summary>
    private void SaveWindowSize()
    {
        if (WindowState == WindowState.Maximized)
        {
            _savedWindowState = WindowState.Maximized;
        }
        else if (WindowState == WindowState.Normal)
        {
            _savedWindowState = WindowState.Normal;
            if (Width > 0 && Height > 0)
            {
                _savedWidth = Width;
                _savedHeight = Height;
            }
        }

        try
        {
            var userSettingsService = App.Services?.GetService<IUserSettingsService>();
            userSettingsService?.Update(s =>
            {
                s.ProfileSettingsWindowIsMaximized = _savedWindowState == WindowState.Maximized;
                if (_savedWidth.HasValue)
                {
                    s.ProfileSettingsWindowWidth = _savedWidth.Value;
                }

                if (_savedHeight.HasValue)
                {
                    s.ProfileSettingsWindowHeight = _savedHeight.Value;
                }

                s.ProfileSettingsSidebarWidth = SavedSidebarWidth;
            });
            _ = userSettingsService?.SaveAsync();
        }
        catch
        {
            // Ignore settings save errors in design-time or unit-test environments
        }
    }
}
